using System.Diagnostics;
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
    private static readonly List<(Guid Biller, long ID)> invoices = [];
    private static Guid referenceBiller;
    private static long referenceInvoice;
    private const string ReferenceTerms = "Niobium.Invoicing CI reference fixture";

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
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddDatabase();
        collection.AddSingleton<IAzureTableClientFactory>(new TableFactory(tables));
        services = collection.BuildServiceProvider();
        bus = new ServiceBusClient(connection, new ServiceBusClientOptions { TransportType = ServiceBusTransportType.AmqpWebSockets });
        _ = Required("TEST_SIGNING_KEY");
        _ = Required("TEST_TOKEN_ISSUER");
        _ = Required("TEST_TOKEN_AUDIENCE");
        await EnsureReferenceInvoice();
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        List<Exception> failures = [];
        // Delete only this run's randomly scoped records, never tables or another run's rows.
        if (tables != null)
        {
            foreach ((Guid owningBiller, long id) in invoices)
            {
                try
                {
                    await DeleteOwnedInvoice(owningBiller, id);
                }
                catch (Exception error) { failures.Add(error); }
            }
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
        await VerifyReadOnlyClient(id, linkToken);
    }

    [TestMethod]
    public async Task Deployed_queue_consumer_persists_and_updates_through_the_real_test_queue()
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
            NotifyBillee = false
        };
        await using ServiceBusSender sender = bus.CreateSender("issueinvoicecommand");
        await sender.SendMessageAsync(new ServiceBusMessage(JsonMarshaller.Marshall(command)) { MessageId = Guid.NewGuid().ToString(), ContentType = "application/json" });
        await Eventually(async () => (await Read(id)).GetInt64(nameof(Invoice.GrandTotalCents)) == 11500L, "The real queue consumer did not persist the invoice.");
        TableEntity saved = await Read(id);
        Assert.AreEqual("ready-v1", saved.GetString(nameof(Invoice.IssuanceState)));
        Assert.IsFalse(saved.ContainsKey(nameof(Invoice.Token)));
        Assert.IsFalse(saved.ContainsKey(nameof(Invoice.Delivered)));
        // Observe a second completed processing cycle without competing with the real notification consumer.
        command.InvoiceItems[0].Quantity = 2;
        await sender.SendMessageAsync(new ServiceBusMessage(JsonMarshaller.Marshall(command)) { MessageId = Guid.NewGuid().ToString(), ContentType = "application/json" });
        await Eventually(async () => (await Read(id)).GetInt64(nameof(Invoice.GrandTotalCents)) == 23000L, "The real queue consumer did not persist the update.");
        Assert.AreEqual("ready-v1", (await Read(id)).GetString(nameof(Invoice.IssuanceState)));
    }

    [TestMethod]
    public async Task Real_test_notification_service_accepts_an_invoice_for_the_approved_mailbox()
    {
        string recipient = Required("TEST_NOTIFICATION_RECIPIENT");
        Assert.IsTrue(System.Net.Mail.MailAddress.TryCreate(recipient, out _), "A valid approved test mailbox is required.");
        Guid sender = referenceBiller;
        TableEntity fixture = await Read(referenceInvoice, sender);
        Guid group = fixture.GetGuid(nameof(Invoice.Tenant)) ?? throw new InvalidOperationException("The existing test invoice has no tenant.");
        long id = await SeedInvoice(sender, group, recipient);
        IssueInvoiceCommand command = new()
        {
            Tenant = group,
            BillerID = sender,
            BilleeID = customer,
            InvoiceID = id,
            Billee = Customer(sender, recipient),
            InvoiceItems = Request(id).InvoiceItems,
            NotifyBillee = true
        };
        await using ServiceBusSender incoming = bus.CreateSender("issueinvoicecommand");
        await incoming.SendMessageAsync(new ServiceBusMessage(JsonMarshaller.Marshall(command))
        {
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json"
        });
        await Eventually(async () => (await Read(id, sender)).ContainsKey(nameof(Invoice.Delivered)), "The real notification enqueue was not acknowledged.");
        TableEntity saved = await Read(id, sender);
        Assert.AreEqual("frozen-v1", saved.GetString(nameof(Invoice.IssuanceState)));
        Assert.IsTrue(saved.GetString(nameof(Invoice.RecipientEmail)) == recipient, "The stored recipient must match the approved test mailbox.");
        string? token = saved.GetString(nameof(Invoice.Token));
        Assert.IsFalse(string.IsNullOrWhiteSpace(token));
        using HttpResponseMessage html = await http.GetAsync($"/{sender}/invoices/{id}?token={token}");
        Assert.AreEqual(HttpStatusCode.OK, html.StatusCode);
        // Delivered is an enqueue acknowledgement, not a provider receipt. Do not steal the real consumer's messages.
    }

    [TestMethod]
    public async Task Fixture_collision_leaves_the_existing_invoice_unchanged_and_unclaimed_for_cleanup()
    {
        long id = await SeedInvoice();
        TableEntity existing = await Read(id);
        int trackedCount = invoices.Count;
        RequestFailedException collision = await Assert.ThrowsAsync<RequestFailedException>(() => SeedInvoice(invoiceID: id));
        Assert.AreEqual(409, collision.Status);
        Assert.AreEqual(trackedCount, invoices.Count, "A failed creation must not schedule the existing row for cleanup.");
        Assert.AreEqual(existing.ETag, (await Read(id)).ETag, "A fixture collision must not replace the existing invoice.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Reference_fixture_is_retained_only_after_successful_issuance(bool invalidRequest)
    {
        int trackedCount = invoices.Count;
        long id = await SeedInvoice();
        IssueInvoiceRequest request = Request(id);
        if (invalidRequest)
            request.InvoiceItems[0].UnitPriceCurrency = "AUD";
        try
        {
            if (invalidRequest)
            {
                HttpRequestException failure = await Assert.ThrowsAsync<HttpRequestException>(() => IssueReferenceInvoice(request));
                Assert.AreEqual(HttpStatusCode.BadRequest, failure.StatusCode);
                NullableResponse<TableEntity> remaining = await tables.GetTableClient(nameof(Invoice))
                    .GetEntityIfExistsAsync<TableEntity>(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(id));
                Assert.IsFalse(remaining.HasValue, "Failed reference issuance must remove its acknowledged seed.");
            }
            else
            {
                await IssueReferenceInvoice(request);
                Assert.AreEqual("ready-v1", (await Read(id)).GetString(nameof(Invoice.IssuanceState)));
            }
            Assert.AreEqual(trackedCount, invoices.Count, "Only successfully issued references are retained; failed seeds are already cleaned.");
        }
        finally
        {
            NullableResponse<TableEntity> remaining = await tables.GetTableClient(nameof(Invoice))
                .GetEntityIfExistsAsync<TableEntity>(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(id));
            if (remaining.HasValue)
                await DeleteOwnedInvoice(biller, id);
            invoices.Remove((biller, id));
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("invalid-test-function-key")]
    public async Task Debug_email_route_rejects_requests_without_a_valid_function_key(string? functionKey)
    {
        // An existing, ready invoice distinguishes route absence from a missing-invoice result.
        // A blank recipient also prevents a notification if the authorization assertion regresses.
        long id = await SeedInvoice(recipient: string.Empty);
        using HttpResponseMessage issued = await Post(Request(id), Token(biller, tenant));
        Assert.AreEqual(HttpStatusCode.OK, issued.StatusCode);
        TableEntity before = await Read(id);
        using HttpRequestMessage request = new(HttpMethod.Get, $"/email/{biller}/invoices/{id}");
        if (functionKey != null)
            request.Headers.Add("x-functions-key", functionKey);
        using HttpResponseMessage response = await http.SendAsync(request);
        // Q26 permits a key-guarded endpoint. Keep this reviewed contract compatible with its
        // absent predecessor so restoration can pass the immutable main harness without bypasses.
        Assert.IsTrue(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized,
            $"Expected an absent route or Functions key denial, received {(int)response.StatusCode}.");
        Assert.AreEqual(before.ETag, (await Read(id)).ETag, "A rejected request must not change the invoice.");
    }

    private static async Task VerifyReadOnlyClient(long id, string token)
    {
        string root = Required("GITHUB_WORKSPACE");
        ProcessStartInfo start = new("python")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(root, "scripts", "read-only-deployment.py"));
        start.ArgumentList.Add("--require-fixture");
        start.ArgumentList.Add("--report");
        start.ArgumentList.Add(Path.Combine(root, "artifacts", "deployed", "read-only.json"));
        start.Environment["READONLY_API_URL"] = Required("TEST_API_URL");
        start.Environment["READONLY_EXPECTED_COMMIT"] = Required("TEST_EXPECTED_COMMIT");
        start.Environment["READONLY_INVOICE_BILLER_ID"] = biller.ToString();
        start.Environment["READONLY_INVOICE_ID"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["READONLY_INVOICE_TOKEN"] = token;
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the read-only integration client.");
        await Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, "The production read-only client failed against test; inspect the read-only report artifact.");
    }

    private static async Task EnsureReferenceInvoice()
    {
        string? configuredBiller = Environment.GetEnvironmentVariable("TEST_INVOICE_BILLER");
        string? configuredID = Environment.GetEnvironmentVariable("TEST_INVOICE_ID");
        if (!string.IsNullOrWhiteSpace(configuredBiller) || !string.IsNullOrWhiteSpace(configuredID))
        {
            referenceBiller = Guid.Parse(Required("TEST_INVOICE_BILLER"));
            referenceInvoice = long.Parse(Required("TEST_INVOICE_ID"), System.Globalization.CultureInfo.InvariantCulture);
            _ = await Read(referenceInvoice, referenceBiller);
        }
        else
        {
            TableEntity? reference = null;
            await foreach (TableEntity row in tables.GetTableClient(nameof(Invoice)).QueryAsync<TableEntity>(
                TableClient.CreateQueryFilter($"Terms eq {ReferenceTerms}"), maxPerPage: 1))
            {
                reference = row;
                break;
            }
            if (reference != null)
            {
                referenceBiller = Guid.Parse(reference.PartitionKey);
                referenceInvoice = long.Parse(reference.RowKey, System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                // Reuse a real test biller when an existing invoice identifies one. Only test data is read.
                Guid owner = biller;
                Guid group = tenant;
                await foreach (TableEntity existing in tables.GetTableClient(nameof(Invoice)).QueryAsync<TableEntity>(maxPerPage: 1))
                {
                    if (Guid.TryParse(existing.PartitionKey, out Guid candidate) && existing.GetGuid(nameof(Invoice.Tenant)) is Guid existingGroup)
                    {
                        owner = candidate;
                        group = existingGroup;
                        break;
                    }
                }
                referenceBiller = owner;
                referenceInvoice = await SeedInvoice(owner, group, Required("TEST_NOTIFICATION_RECIPIENT"));
                IssueInvoiceRequest request = Request(referenceInvoice);
                request.BillerID = owner;
                request.Tenant = group;
                request.Terms = ReferenceTerms;
                await IssueReferenceInvoice(request);
            }
        }
        string path = Path.Combine(Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? Directory.GetCurrentDirectory(), "artifacts", "deployed", "test-invoice-fixture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            biller = referenceBiller,
            invoice = referenceInvoice
        }));
        // IDs are synthetic test-fixture metadata. Never print recipient addresses or link tokens.
        Console.WriteLine($"Persistent test fixture: TEST_INVOICE_BILLER={referenceBiller}; TEST_INVOICE_ID={referenceInvoice}");
    }

    private static async Task IssueReferenceInvoice(IssueInvoiceRequest request)
    {
        try
        {
            using HttpResponseMessage response = await Post(request, Token(request.BillerID, request.Tenant));
            if (response.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException("The persistent test invoice could not be issued.", null, response.StatusCode);
            invoices.Remove((request.BillerID, request.InvoiceID));
        }
        catch (Exception issuanceError)
        {
            // Initialize can fail before framework ClassCleanup runs. Clean this acknowledged seed here.
            try
            {
                await DeleteOwnedInvoice(request.BillerID, request.InvoiceID);
                invoices.Remove((request.BillerID, request.InvoiceID));
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("Reference issuance and its owned-fixture cleanup failed.", issuanceError, cleanupError);
            }
            throw;
        }
    }

    private static async Task DeleteOwnedInvoice(Guid owningBiller, long id)
    {
        TableEntity owned = await Read(id, owningBiller);
        Assert.IsTrue(owned.GetGuid(nameof(Invoice.Billee)) == customer, "Refusing to delete an unrelated invoice.");
        TableClient items = tables.GetTableClient(nameof(InvoiceItem));
        await foreach (TableEntity item in items.QueryAsync<TableEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {InvoiceItem.BuildPartitionKey(id)}")))
        {
            Assert.AreEqual(itemSubject, item.GetString(nameof(InvoiceItem.Subject)), "Refusing to delete an unrelated item.");
            await items.DeleteEntityAsync(item.PartitionKey, item.RowKey, item.ETag);
        }
        await tables.GetTableClient(nameof(Invoice)).DeleteEntityAsync(owned.PartitionKey, owned.RowKey, owned.ETag);
    }

    private static async Task<long> SeedInvoice(Guid? issuer = null, Guid? group = null, string? recipient = null, long? invoiceID = null)
    {
        long id = invoiceID ?? Invoice.ParseID(DateTimeOffset.UtcNow.AddMilliseconds(invoices.Count));
        await foreach (TableEntity _ in tables.GetTableClient(nameof(InvoiceItem)).QueryAsync<TableEntity>(
            TableClient.CreateQueryFilter($"PartitionKey eq {InvoiceItem.BuildPartitionKey(id)}"), maxPerPage: 1))
        {
            Assert.Fail("Generated invoice ID collides with existing items; refusing to modify them.");
        }
        Guid owningBiller = issuer ?? biller;
        Invoice invoice = Invoice.BuildNew(id, new Biller
        {
            PartitionKey = group ?? tenant,
            RowKey = owningBiller,
            BusinessName = "Synthetic CI biller",
            TaxRatePercentile = 1500,
            Currency = "NZD",
            Culture = "en-NZ",
            TimeZone = "Pacific/Auckland"
        }, Customer(owningBiller, recipient));
        await services.GetRequiredService<IRepository<Invoice>>().CreateAsync([invoice]);
        // A rejected or uncertain creation must never claim a pre-existing row for cleanup.
        invoices.Add((owningBiller, id));
        return id;
    }

    private static Billee Customer(Guid? issuer = null, string? recipient = null) => new() { Biller = issuer ?? biller, ID = customer, Name = "Synthetic CI customer", Email = recipient ?? "unused@example.invalid", Currency = "NZD", Culture = "en-NZ", TimeZone = "Pacific/Auckland" };
    private static IssueInvoiceRequest Request(long id) => new()
    {
        Tenant = tenant,
        BillerID = biller,
        BilleeID = customer,
        InvoiceID = id,
        Terms = "<script>literal</script>",
        InvoiceItems = [new InvoiceItem { Invoice = Invoice.ParseID(id), ID = 1, Subject = itemSubject, UnitPriceCents = 10000, Quantity = 1, UnitPriceCurrency = "NZD", LineTotalCurrency = "NZD" }]
    };
    private static async Task<TableEntity> Read(long id, Guid? issuer = null) => (await tables.GetTableClient(nameof(Invoice)).GetEntityAsync<TableEntity>(Invoice.BuildPartitionKey(issuer ?? biller), Invoice.BuildRowKey(id))).Value;
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
