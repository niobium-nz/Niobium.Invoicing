using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Niobium.Database.StorageTable;

namespace Niobium.Invoicing.Deployment.Tests;

// This separately invoked project NEVER skips missing deployment configuration.
[TestClass]
[DoNotParallelize]
public sealed class DeployedInvoiceTests
{
    private static HttpClient http = null!;
    private static TableServiceClient tables = null!;
    private static ServiceBusClient bus = null!;
    private static ServiceProvider services = null!;
    private static readonly Guid tenant = Guid.NewGuid();
    private static readonly Guid biller = Guid.NewGuid();
    private static readonly Guid customer = Guid.NewGuid();
    private static readonly string itemSubject = $"Synthetic CI service {biller:N}";
    private static readonly List<long> invoices = [];
    private static readonly List<ServiceBusReceivedMessage> unrelated = [];
    private static ServiceBusReceiver? receiver;

    [ClassInitialize]
    public static async Task Initialize(TestContext context)
    {
        Assert.AreEqual("test", Required("TEST_ENVIRONMENT"), "Only the test environment is allowed.");
        Uri endpoint = new(Required("TEST_API_URL"));
        Assert.IsTrue(endpoint.Scheme == "https" && endpoint.Host.EndsWith(".azurewebsites.net", StringComparison.Ordinal)
            && endpoint.Host.Contains("test", StringComparison.OrdinalIgnoreCase), "A test Azure Functions hostname is required.");
        string account = Required("TEST_STORAGE_ACCOUNT");
        Assert.IsTrue(account.EndsWith("test", StringComparison.Ordinal), "A test storage account is required.");
        string connection = Required("TEST_SERVICEBUS_CONNECTION");
        string busHost = ServiceBusConnectionStringProperties.Parse(connection).FullyQualifiedNamespace;
        Assert.IsTrue(busHost.EndsWith("test.servicebus.windows.net", StringComparison.Ordinal), "A test Service Bus namespace is required.");
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(40) };
        string expected = Required("TEST_EXPECTED_COMMIT");
        Assert.AreEqual(40, expected.Length, "The exact tested commit is required.");
        await Eventually(async () =>
        {
            try
            {
                using HttpResponseMessage response = await http.GetAsync("/health");
                if (!response.IsSuccessStatusCode)
                    return false;
                using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.TryGetProperty("commit", out JsonElement commit) && commit.GetString() == expected;
            }
            catch (HttpRequestException) { return false; }
            catch (TaskCanceledException) { return false; }
            catch (JsonException) { return false; }
        }, "The deployed binary did not report the expected commit.");
        tables = new TableServiceClient(new Uri($"https://{account}.table.core.windows.net"), new TableSharedKeyCredential(account, Required("TEST_STORAGE_KEY")));
        await tables.CreateTableIfNotExistsAsync(nameof(Invoice));
        await tables.CreateTableIfNotExistsAsync(nameof(InvoiceItem));
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddDatabase();
        collection.AddSingleton<IAzureTableClientFactory>(new TableFactory(tables));
        services = collection.BuildServiceProvider();
        bus = new ServiceBusClient(connection, new ServiceBusClientOptions { TransportType = ServiceBusTransportType.AmqpWebSockets });
        _ = Required("TEST_SIGNING_KEY");
        _ = Required("TEST_TOKEN_ISSUER");
        _ = Required("TEST_TOKEN_AUDIENCE");
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        List<Exception> failures = [];
        // Delete only this run's randomly scoped records, never tables or another run's rows.
        if (tables != null)
        {
            foreach (long id in invoices)
            {
                try
                {
                    TableClient items = tables.GetTableClient(nameof(InvoiceItem));
                    await foreach (TableEntity item in items.QueryAsync<TableEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {InvoiceItem.BuildPartitionKey(id)}")))
                    {
                        Assert.AreEqual(itemSubject, item.GetString(nameof(InvoiceItem.Subject)), "Refusing to delete an unrelated item.");
                        await items.DeleteEntityAsync(item.PartitionKey, item.RowKey, item.ETag);
                    }
                    await tables.GetTableClient(nameof(Invoice)).DeleteEntityAsync(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(id));
                }
                catch (Exception error) { failures.Add(error); }
            }
        }
        if (receiver != null)
        {
            foreach (ServiceBusReceivedMessage message in unrelated)
            {
                try
                {
                    await receiver.AbandonMessageAsync(message);
                }
                catch (ServiceBusException error) when (error.Reason == ServiceBusFailureReason.MessageLockLost) { }
                catch (Exception error) { failures.Add(error); }
            }
            await receiver.DisposeAsync();
        }
        if (bus != null)
            await bus.DisposeAsync();
        services?.Dispose();
        http?.Dispose();
        if (failures.Count > 0)
            throw new AggregateException("Synthetic test fixture cleanup failed.", failures);
    }

    [TestMethod]
    [DataRow("missing", 401)]
    [DataRow("invalid", 401)]
    [DataRow("wrong-biller", 403)]
    [DataRow("wrong-tenant", 403)]
    public async Task Deployed_middleware_and_handler_reject_unauthorized_issuance(string mode, int status)
    {
        IssueInvoiceRequest request = Request(Invoice.ParseID(DateTimeOffset.UtcNow));
        string? token = mode == "missing" ? null : mode == "invalid" ? "invalid-test-token" : Token(
            mode == "wrong-biller" ? Guid.NewGuid() : biller, mode == "wrong-tenant" ? Guid.NewGuid() : tenant);
        using HttpResponseMessage response = await Post(request, token);
        Assert.AreEqual(status, (int)response.StatusCode);
    }

    [TestMethod]
    public async Task Deployed_HTTP_persists_validates_replays_and_renders_saved_token_HTML()
    {
        long id = await SeedInvoice();
        IssueInvoiceRequest request = Request(id);
        request.NotifyBillee = true; // HTTP must still suppress notification.
        using (HttpResponseMessage first = await Post(request, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        TableEntity saved = await Read(id);
        Assert.AreEqual(11500L, saved.GetInt64(nameof(Invoice.GrandTotalCents)));
        Assert.AreEqual("ready-v1", saved.GetString(nameof(Invoice.IssuanceState)));
        Assert.IsFalse(saved.ContainsKey(nameof(Invoice.Token)));
        using (HttpResponseMessage replay = await Post(request, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(saved.ETag, (await Read(id)).ETag, "Exact replay must not rewrite the invoice.");
        request.InvoiceItems[0].UnitPriceCurrency = "AUD";
        using (HttpResponseMessage invalid = await Post(request, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        request.InvoiceItems[0].UnitPriceCurrency = "NZD";
        string linkToken = Guid.NewGuid().ToString("N");
        saved[nameof(Invoice.Token)] = linkToken;
        await tables.GetTableClient(nameof(Invoice)).UpdateEntityAsync(saved, saved.ETag, TableUpdateMode.Merge);
        using (HttpResponseMessage denied = await http.GetAsync($"/{biller}/invoices/{id}?token=invalid"))
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        using HttpResponseMessage view = await http.GetAsync($"/{biller}/invoices/{id}?token={linkToken}");
        Assert.AreEqual(HttpStatusCode.OK, view.StatusCode);
        Assert.AreEqual("text/html", view.Content.Headers.ContentType?.MediaType);
        string html = await view.Content.ReadAsStringAsync();
        StringAssert.Contains(html, "Synthetic CI customer");
        StringAssert.Contains(html, "&lt;script&gt;literal&lt;/script&gt;");
        request.InvoiceItems[0].Quantity = 2;
        using HttpResponseMessage changed = await Post(request, Token(biller, tenant));
        Assert.AreEqual(HttpStatusCode.Conflict, changed.StatusCode);
    }

    [TestMethod]
    public async Task Deployed_queue_consumer_emits_stable_notifications_on_exact_retry()
    {
        long id = await SeedInvoice();
        IssueInvoiceRequest request = Request(id);
        IssueInvoiceCommand command = new()
        {
            Tenant = tenant,
            BillerID = biller,
            BilleeID = customer,
            InvoiceID = id,
            Billee = Customer(),
            InvoiceItems = request.InvoiceItems,
            Terms = request.Terms,
            NotifyBillee = true
        };
        receiver = bus.CreateReceiver("notifycommand", new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
        await using ServiceBusSender sender = bus.CreateSender("issueinvoicecommand");
        string messageID = Invoice.BuildFullID(biller, id);
        await sender.SendMessageAsync(new ServiceBusMessage(JsonMarshaller.Marshall(command)) { MessageId = Guid.NewGuid().ToString(), ContentType = "application/json" });
        string first = await Notification(messageID);
        await Eventually(async () => (await Read(id)).ContainsKey(nameof(Invoice.Delivered)), "Enqueue completion was not persisted.");
        TableEntity saved = await Read(id);
        Assert.AreEqual("frozen-v1", saved.GetString(nameof(Invoice.IssuanceState)));
        Assert.IsFalse(string.IsNullOrWhiteSpace(saved.GetString(nameof(Invoice.Token))));
        await sender.SendMessageAsync(new ServiceBusMessage(JsonMarshaller.Marshall(command)) { MessageId = Guid.NewGuid().ToString(), ContentType = "application/json" });
        Assert.IsTrue(string.Equals(first, await Notification(messageID), StringComparison.Ordinal), "Exact retry must emit identical notification content (payloads omitted to protect link tokens).");
        Assert.IsTrue(string.Equals(saved.GetString(nameof(Invoice.Token)), (await Read(id)).GetString(nameof(Invoice.Token)), StringComparison.Ordinal), "Exact retry must preserve the link token.");
        using JsonDocument notification = JsonDocument.Parse(first);
        Assert.AreEqual(messageID, notification.RootElement.GetProperty("id").GetString());
        Assert.AreEqual("sink@example.invalid", notification.RootElement.GetProperty("destination").GetString());
    }

    [TestMethod]
    public async Task Debug_email_route_is_absent_from_deployment()
    {
        using HttpResponseMessage response = await http.GetAsync($"/email/{Guid.NewGuid()}/invoices/1");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<string> Notification(string expected)
    {
        string? body = null;
        await Eventually(async () =>
        {
            ServiceBusReceivedMessage? message = await receiver!.ReceiveMessageAsync(TimeSpan.FromSeconds(2));
            if (message == null)
                return false;
            if (message.MessageId != expected)
            {
                unrelated.Add(message); // Never complete another run's message.
                Assert.IsTrue(unrelated.Count <= 100, "The controlled test sink has too many unrelated messages.");
                return false;
            }
            body = message.Body.ToString();
            await receiver.CompleteMessageAsync(message);
            return true;
        }, "No matching notification arrived in the isolated test sink.");
        return body!;
    }

    private static async Task<long> SeedInvoice()
    {
        long id = Invoice.ParseID(DateTimeOffset.UtcNow.AddMilliseconds(invoices.Count));
        await foreach (TableEntity _ in tables.GetTableClient(nameof(InvoiceItem)).QueryAsync<TableEntity>(
            TableClient.CreateQueryFilter($"PartitionKey eq {InvoiceItem.BuildPartitionKey(id)}"), maxPerPage: 1))
        {
            Assert.Fail("Generated invoice ID collides with existing items; refusing to modify them.");
        }
        invoices.Add(id);
        Invoice invoice = Invoice.BuildNew(id, new Biller
        {
            PartitionKey = tenant,
            RowKey = biller,
            BusinessName = "Synthetic CI biller",
            TaxRatePercentile = 1500,
            Currency = "NZD",
            Culture = "en-NZ",
            TimeZone = "Pacific/Auckland"
        }, Customer());
        await services.GetRequiredService<IRepository<Invoice>>().CreateAsync([invoice]);
        return id;
    }

    private static Billee Customer() => new() { Biller = biller, ID = customer, Name = "Synthetic CI customer", Email = "sink@example.invalid", Currency = "NZD", Culture = "en-NZ", TimeZone = "Pacific/Auckland" };
    private static IssueInvoiceRequest Request(long id) => new()
    {
        Tenant = tenant,
        BillerID = biller,
        BilleeID = customer,
        InvoiceID = id,
        Terms = "<script>literal</script>",
        InvoiceItems = [new InvoiceItem { Invoice = Invoice.ParseID(id), ID = 1, Subject = itemSubject, UnitPriceCents = 10000, Quantity = 1, UnitPriceCurrency = "NZD", LineTotalCurrency = "NZD" }]
    };
    private static async Task<TableEntity> Read(long id) => (await tables.GetTableClient(nameof(Invoice)).GetEntityAsync<TableEntity>(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(id))).Value;
    private static async Task<HttpResponseMessage> Post(IssueInvoiceRequest request, string? token)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, "/invoices")
        {
            Content = new StringContent(JsonMarshaller.Marshall(request), Encoding.UTF8, "application/json")
        };
        if (token != null)
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(message);
    }
    private static string Token(Guid user, Guid group) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Required("TEST_TOKEN_ISSUER"),
        Audience = Required("TEST_TOKEN_AUDIENCE"),
        Expires = DateTime.UtcNow.AddMinutes(10),
        Subject = new ClaimsIdentity([new Claim(ClaimTypes.Sid, user.ToString()), new Claim(ClaimTypes.GroupSid, group.ToString())]),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Required("TEST_SIGNING_KEY"))), SecurityAlgorithms.HmacSha256)
    });
    private static string Required(string key) => Environment.GetEnvironmentVariable(key) is { Length: > 0 } value ? value : throw new InvalidOperationException($"Required deployment test setting is missing: {key}");
    private static async Task Eventually(Func<Task<bool>> condition, string failure)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(3);
        do
        {
            if (await condition())
                return;
            await Task.Delay(TimeSpan.FromSeconds(2));
        } while (DateTimeOffset.UtcNow < deadline);
        Assert.Fail(failure);
    }
    private sealed class TableFactory(TableServiceClient client) : IAzureTableClientFactory
    {
        public Task<TableServiceClient> CreateClientAsync(IEnumerable<DatabasePermissions> permissions, string table, string? partition = null, CancellationToken cancellationToken = default)
        {
            if (table is not (nameof(Invoice) or nameof(InvoiceItem)))
                throw new InvalidOperationException("Unexpected test table.");
            return Task.FromResult(client);
        }
    }
}
