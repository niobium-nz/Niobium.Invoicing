using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Server;
using Niobium.Platform.Identity;
using System.Reflection;
using System.Security.Claims;
using System.Text;

namespace Niobium.Invoicing.Integration.Tests;

[TestClass]
public sealed class HttpBoundaryTests
{
    private const string Secret = "synthetic-local-test-signing-key-never-used-in-production";
    private const string Issuer = "synthetic-invoicing-test";
    private const string Audience = "synthetic-invoicing-client";

    [TestMethod]
    [DataRow("missing-user", 401)]
    [DataRow("missing-group", 401)]
    [DataRow("wrong-user", 403)]
    [DataRow("wrong-group", 403)]
    [DataRow("expired-invoice", 403)]
    [DataRow("invalid-invoice", 400)]
    public async Task Authorization_and_date_results_execute_as_expected_HTTP_status(string scenario, int expected)
    {
        IssueInvoiceRequest request = Request();
        Mock<UpsertFlow> upsert = new(MockBehavior.Strict, null!, null!, null!, null!);
        Mock<EmailFlow> email = new(MockBehavior.Strict, null!, null!, null!, null!, null!);
        NewInvoice handler = new(new IssueFlow(upsert.Object, email.Object));
        List<Claim> claims = [new(ClaimTypes.Sid, request.BillerID.ToString()), new(ClaimTypes.GroupSid, request.Tenant.ToString())];
        if (scenario == "missing-user")
        {
            claims.RemoveAt(0);
        }
        if (scenario == "missing-group")
        {
            claims.RemoveAt(1);
        }
        if (scenario == "wrong-user")
        {
            claims[0] = new Claim(ClaimTypes.Sid, Guid.NewGuid().ToString());
        }
        if (scenario == "wrong-group")
        {
            claims[1] = new Claim(ClaimTypes.GroupSid, Guid.NewGuid().ToString());
        }
        if (scenario == "expired-invoice")
        {
            request.InvoiceID = Invoice.ParseID(DateTimeOffset.UtcNow.AddHours(-1));
        }
        if (scenario == "invalid-invoice")
        {
            request.InvoiceID = long.MaxValue;
        }
        PrincipalParser parser = Parser();
        ClaimsPrincipal principal = await parser.ParseAsync(Token(claims));
        ServiceCollection services = new();
        services.AddLogging();
        services.AddControllers();
        using ServiceProvider provider = services.BuildServiceProvider();
        DefaultHttpContext context = new()
        {
            User = principal,
            RequestServices = provider
        };
        context.Response.Body = new MemoryStream();
        IActionResult result = await handler.Run(context.Request, request, CancellationToken.None);
        await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));
        Assert.AreEqual(expected, context.Response.StatusCode);
        upsert.Verify(x => x.RunAsync(It.IsAny<IssueInvoiceRequest>(), It.IsAny<Billee?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow("signature")]
    [DataRow("issuer")]
    [DataRow("audience")]
    [DataRow("expiry")]
    public async Task Installed_framework_rejects_invalid_access_tokens(string invalid)
    {
        string token = Token([], invalid);
        ApplicationException denied = await Assert.ThrowsAsync<ApplicationException>(() => Parser().ParseAsync(token));
        Assert.AreEqual(401, denied.ErrorCode);
    }

    [TestMethod]
    public async Task Matching_access_token_reaches_shared_issue_flow_with_HTTP_notifications_disabled()
    {
        IssueInvoiceRequest request = Request();
        request.NotifyBillee = true;
        Mock<UpsertFlow> upsert = new(MockBehavior.Strict, null!, null!, null!, null!);
        upsert.Setup(x => x.RunAsync(request, null, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Mock<EmailFlow> email = new(MockBehavior.Strict, null!, null!, null!, null!, null!);
        DefaultHttpContext context = new()
        {
            User = await Parser().ParseAsync(Token([new Claim(ClaimTypes.Sid, request.BillerID.ToString()), new Claim(ClaimTypes.GroupSid, request.Tenant.ToString())]))
        };
        IActionResult result = await new NewInvoice(new IssueFlow(upsert.Object, email.Object)).Run(context.Request, request, CancellationToken.None);
        Assert.IsInstanceOfType<OkResult>(result);
        Assert.IsFalse(request.NotifyBillee);
        upsert.Verify(x => x.RunAsync(request, null, It.IsAny<CancellationToken>()), Times.Once);
        email.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void Manual_email_function_preserves_its_route_and_requires_a_Functions_key()
    {
        MethodInfo method = typeof(EmailInvoice).GetMethod(nameof(EmailInvoice.Run))!;
        Assert.AreEqual("EmailInvoice", method.GetCustomAttribute<FunctionAttribute>()?.Name);
        HttpTriggerAttribute trigger = method.GetParameters()[0].GetCustomAttribute<HttpTriggerAttribute>()!;
        Assert.IsNotNull(trigger);
        Assert.AreEqual(AuthorizationLevel.Function, trigger.AuthLevel);
        Assert.AreEqual("email/{issuer}/invoices/{invoice}", trigger.Route);
        CollectionAssert.AreEqual(new[] { "get" }, trigger.Methods);
        int emailRoutes = typeof(EmailInvoice).Assembly.GetTypes().SelectMany(t => t.GetMethods())
            .SelectMany(m => m.GetParameters()).Select(p => p.GetCustomAttribute<HttpTriggerAttribute>())
            .Count(a => a?.Route?.StartsWith("email/", StringComparison.Ordinal) == true);
        Assert.AreEqual(1, emailRoutes, "No additional email route may bypass the key-protected trigger.");
    }

    private static IssueInvoiceRequest Request() => new()
    {
        Tenant = Guid.NewGuid(),
        BillerID = Guid.NewGuid(),
        BilleeID = Guid.NewGuid(),
        InvoiceID = Invoice.ParseID(DateTimeOffset.UtcNow),
        InvoiceItems = []
    };

    private static PrincipalParser Parser() => new(Microsoft.Extensions.Options.Options.Create(new IdentityServiceOptions
    {
        AccessTokenSecret = Secret,
        AccessTokenIssuer = Issuer,
        AccessTokenAudience = Audience
    }));

    private static string Token(IEnumerable<Claim> claims, string? invalid = null)
    {
        DateTime expires = invalid == "expiry" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1);
        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = invalid == "issuer" ? "wrong-issuer" : Issuer,
            Audience = invalid == "audience" ? "wrong-audience" : Audience,
            Expires = expires,
            IssuedAt = expires.AddHours(-2),
            NotBefore = expires.AddHours(-2),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(invalid == "signature" ? Secret + "wrong" : Secret)), SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
