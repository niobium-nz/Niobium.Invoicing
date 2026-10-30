using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Niobium.Database.StorageTable;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Server;
using Niobium.Messaging;
using Niobium.Notification;
using Niobium.Profile;

namespace Niobium.Invoicing.Integration.Tests;

[TestClass]
[DoNotParallelize]
public sealed class StorageFlowTests
{
    private static IHost? host;
    private static TableServiceClient? tableService;
    private static readonly string prefix = "InvTest" + Guid.NewGuid().ToString("N");
    private static readonly Dictionary<(Guid, Guid), Biller> profiles = [];
    private static readonly List<MessagingEntry<NotifyCommand>> messages = [];
    // Published Azurite development account key. This is an emulator fixture, never a cloud credential.
    private const string EmulatorKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    [ClassInitialize]
    public static async Task Initialize(TestContext _)
    {
        string? endpoint = Environment.GetEnvironmentVariable("NIOBIUM_INVOICING_TEST_TABLE_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }
        Uri uri = new(endpoint);
        Assert.IsTrue(uri.IsLoopback && uri.Scheme == "http" && string.IsNullOrEmpty(uri.Query), "This suite only permits a task-local emulator.");
        TableClientOptions options = new();
        options.Retry.MaxRetries = 0;
        tableService = new TableServiceClient(uri, new TableSharedKeyCredential("devstoreaccount1", EmulatorKey), options);
        foreach (string table in new[] { nameof(Invoice), nameof(InvoiceItem), nameof(Billee) })
        {
            await tableService.CreateTableAsync(prefix + table);
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BillingOptions:InvoiceTokenSecretSalt"] = "synthetic-integration-test-secret",
            ["BillingOptions:GetInvoiceEndpoint"] = "https://invoice.example.invalid",
            ["AZURE_FUNCTIONS_ENVIRONMENT"] = "Production",
            ["SERVICE_NAME"] = "Niobium.Invoicing.Integration.Tests"
        });
        builder.AddInvoicing();
        builder.Services.Replace(ServiceDescriptor.Singleton<IAzureTableClientFactory>(new LocalFactory(
            new PrefixedTableService(uri, new TableSharedKeyCredential("devstoreaccount1", EmulatorKey), options))));
        Mock<IProfileService<Biller>> profile = new(MockBehavior.Strict);
        profile.Setup(p => p.RetrieveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenant, Guid biller, bool _, CancellationToken cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                return profiles.GetValueOrDefault((tenant, biller));
            });
        builder.Services.Replace(ServiceDescriptor.Singleton(profile.Object));
        Mock<IMessagingBroker<NotifyCommand>> broker = new(MockBehavior.Strict);
        broker.Setup(b => b.EnqueueAsync(It.IsAny<IEnumerable<MessagingEntry<NotifyCommand>>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<MessagingEntry<NotifyCommand>>, CancellationToken>((entries, token) => { token.ThrowIfCancellationRequested(); messages.AddRange(entries); })
            .Returns(Task.CompletedTask);
        builder.Services.Replace(ServiceDescriptor.Singleton(broker.Object));
        host = builder.Build();
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        host?.Dispose();
        if (tableService != null)
        {
            foreach (string table in new[] { nameof(Invoice), nameof(InvoiceItem), nameof(Billee) })
            {
                await tableService.DeleteTableAsync(prefix + table);
            }
        }
    }

    [TestInitialize]
    public void RequireOptIn()
    {
        if (host == null)
        {
            Assert.Inconclusive("Set NIOBIUM_INVOICING_TEST_TABLE_ENDPOINT to an isolated local Azurite endpoint.");
        }
        messages.Clear();
    }

    [TestMethod]
    public async Task Real_registration_and_storage_support_create_edit_and_stale_ETag_rejection()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, CancellationToken.None);
        IRepository<Invoice> repo = host.Services.GetRequiredService<IRepository<Invoice>>();
        Invoice before = (await repo.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        Assert.IsFalse(string.IsNullOrWhiteSpace(before.ETag));
        Assert.AreEqual(InvoiceContent.Ready, before.IssuanceState);
        Assert.AreEqual(11500, before.GrandTotalCents);
        request.InvoiceItems[0].Quantity = 2;
        await host.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, CancellationToken.None);
        Invoice after = (await repo.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        Assert.AreEqual(23000, after.GrandTotalCents);
        Assert.AreNotEqual(before.ETag, after.ETag);
        RequestFailedException conflict = await Assert.ThrowsAsync<RequestFailedException>(() => repo.UpdateAsync([before]));
        Assert.AreEqual(412, conflict.Status);
    }

    [TestMethod]
    public async Task Frozen_replay_reuses_real_stored_token_and_message_and_renders_public_invoice()
    {
        IssueInvoiceRequest request = await NewRequest();
        request.NotifyBillee = true;
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, CancellationToken.None);
        await host.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, CancellationToken.None);
        Assert.HasCount(2, messages);
        Assert.AreEqual(messages[0].ID, messages[1].ID);
        Assert.AreEqual(JsonMarshaller.Marshall(messages[0].Value!), JsonMarshaller.Marshall(messages[1].Value!));
        Invoice stored = (await host.Services.GetRequiredService<IRepository<Invoice>>().RetrieveAsync(
            Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        Assert.AreEqual(InvoiceContent.Frozen, stored.IssuanceState);
        Assert.IsNotNull(stored.Delivered);
        string html = await host.Services.GetRequiredService<HTMLFlow>().RunAsync(request.BillerID, request.InvoiceID, stored.Token!, CancellationToken.None);
        StringAssert.Contains(html, "Synthetic customer");
        ApplicationException denied = await Assert.ThrowsAsync<ApplicationException>(() => host.Services.GetRequiredService<HTMLFlow>()
            .RunAsync(request.BillerID, request.InvoiceID, "wrong-synthetic-token", CancellationToken.None));
        Assert.AreEqual(403, denied.ErrorCode);
    }

    [TestMethod]
    public async Task Competing_preparations_from_the_same_version_have_one_winner()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, CancellationToken.None);
        IDomainRepository<InvoiceDomain, Invoice> domains = host.Services.GetRequiredService<IDomainRepository<InvoiceDomain, Invoice>>();
        InvoiceDomain first = await domains.GetAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID), forceLoad: true);
        _ = await first.GetEntityAsync();
        InvoiceDomain second = await domains.GetAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID), forceLoad: true);
        _ = await second.GetEntityAsync();
        InvoiceItem[] items = await host.Services.GetRequiredService<IRepository<InvoiceItem>>().GetAsync(InvoiceItem.BuildPartitionKey(request.InvoiceID)).ToArrayAsync();
        request.Terms = "Changed content";
        Assert.IsTrue(await first.UpdateAsync(request, items, CancellationToken.None));
        ApplicationException conflict = await Assert.ThrowsAsync<ApplicationException>(() => second.UpdateAsync(request, items, CancellationToken.None));
        Assert.AreEqual(409, conflict.ErrorCode);
        // No worker is left running in this test; release our own acknowledged claim for cleanup.
        await first.FailPreparationAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task Exact_retry_repairs_a_known_partial_item_preparation_before_notification()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, CancellationToken.None);
        IDomainRepository<InvoiceDomain, Invoice> domains = host.Services.GetRequiredService<IDomainRepository<InvoiceDomain, Invoice>>();
        InvoiceDomain domain = await domains.GetAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID), forceLoad: true);
        IRepository<InvoiceItem> itemRepo = host.Services.GetRequiredService<IRepository<InvoiceItem>>();
        InvoiceItem[] oldItems = await itemRepo.GetAsync(InvoiceItem.BuildPartitionKey(request.InvoiceID)).ToArrayAsync();
        request.InvoiceItems.Add(new InvoiceItem
        {
            Invoice = Invoice.ParseID(request.InvoiceID),
            ID = 2,
            Subject = "Second synthetic service",
            UnitPriceCurrency = "NZD",
            LineTotalCurrency = "NZD",
            UnitPriceCents = 1000,
            Quantity = 1
        });
        Assert.IsTrue(await domain.UpdateAsync(request, oldItems, CancellationToken.None));
        await itemRepo.DeleteAsync(oldItems);
        await itemRepo.CreateAsync(request.InvoiceItems.Take(1));
        // All writes above have acknowledged results. Model the recoverable checkpoint after a
        // definitive later-batch rejection; uncertain remote writes must never be released this way.
        await domain.FailPreparationAsync(CancellationToken.None);
        Assert.HasCount(0, messages);
        request.NotifyBillee = true;
        await host.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, CancellationToken.None);
        InvoiceItem[] repaired = await itemRepo.GetAsync(InvoiceItem.BuildPartitionKey(request.InvoiceID)).ToArrayAsync();
        Assert.HasCount(2, repaired);
        Invoice stored = (await host.Services.GetRequiredService<IRepository<Invoice>>().RetrieveAsync(
            Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        Assert.AreEqual(12650, stored.GrandTotalCents);
        Assert.AreEqual(InvoiceContent.Frozen, stored.IssuanceState);
        Assert.HasCount(1, messages);
    }

    private static async Task<IssueInvoiceRequest> NewRequest()
    {
        Guid tenant = Guid.NewGuid();
        Guid biller = Guid.NewGuid();
        Guid customer = Guid.NewGuid();
        long id = Invoice.ParseID(DateTimeOffset.UtcNow);
        profiles[(tenant, biller)] = new Biller { PartitionKey = tenant, RowKey = biller, BusinessName = "Synthetic biller", Currency = "NZD", Culture = "en-NZ", TimeZone = "Pacific/Auckland", TaxRatePercentile = 1500 };
        await host!.Services.GetRequiredService<IRepository<Billee>>().CreateAsync([new Billee
        {
            Biller = biller, ID = customer, Name = "Synthetic customer", Currency = "NZD", Culture = "en-NZ",
            TimeZone = "Pacific/Auckland", Email = "recipient@example.invalid"
        }]);
        return new IssueInvoiceRequest
        {
            Tenant = tenant,
            BillerID = biller,
            BilleeID = customer,
            InvoiceID = id,
            InvoiceItems = [new InvoiceItem { Invoice = Invoice.ParseID(id), ID = 1, Subject = "Synthetic service", UnitPriceCurrency = "NZD", LineTotalCurrency = "NZD", UnitPriceCents = 10000, Quantity = 1 }]
        };
    }

    private sealed class LocalFactory(TableServiceClient client) : IAzureTableClientFactory
    {
        public Task<TableServiceClient> CreateClientAsync(IEnumerable<DatabasePermissions> permissions, string table, string? partition = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (table is not (nameof(Invoice) or nameof(InvoiceItem) or nameof(Billee)))
            {
                throw new InvalidOperationException("Unexpected integration table.");
            }
            return Task.FromResult(client);
        }
    }

    private sealed class PrefixedTableService(Uri endpoint, TableSharedKeyCredential credential, TableClientOptions options)
        : TableServiceClient(endpoint, credential, options)
    {
        public override TableClient GetTableClient(string tableName) => base.GetTableClient(prefix + tableName);
    }
}
