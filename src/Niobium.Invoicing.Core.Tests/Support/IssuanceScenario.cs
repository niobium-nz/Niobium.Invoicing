using Azure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Niobium.Finance;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Options;
using Niobium.Messaging;
using Niobium.Notification;
using Niobium.Profile;

namespace Niobium.Invoicing.Core.Tests.Support;

internal sealed class IssuanceScenario
{
    internal IssueInvoiceRequest Request { get; }
    internal Biller Biller { get; }
    internal Billee Billee { get; }
    internal Invoice? Stored { get; set; }
    internal List<InvoiceItem> Items { get; } = [];
    internal List<MessagingEntry<NotifyCommand>> Messages { get; } = [];
    internal List<CancellationToken> Tokens { get; } = [];
    internal Mock<IRepository<Invoice>> InvoiceRepo { get; } = new(MockBehavior.Strict);
    internal Mock<IRepository<InvoiceItem>> ItemRepo { get; } = new(MockBehavior.Strict);
    internal Mock<IRepository<Billee>> BilleeRepo { get; } = new(MockBehavior.Strict);
    internal Mock<IProfileService<Biller>> Profiles { get; } = new(MockBehavior.Strict);
    internal int InvoiceWrites { get; private set; }
    internal int ItemWrites { get; private set; }
    internal bool FailDelete { get; set; }
    internal bool FailCreate { get; set; }
    internal bool FailInvoiceWrite { get; set; }
    internal bool DropWriteEtags { get; set; }
    internal bool MissingBiller { get; set; }
    internal bool MissingBillee { get; set; }
    internal Action? BeforeInvoiceUpdate { get; set; }
    private int version;

    internal IssuanceScenario()
    {
        Guid tenant = Guid.NewGuid();
        Guid biller = Guid.NewGuid();
        Guid billee = Guid.NewGuid();
        long id = Invoice.ParseID(DateTimeOffset.UtcNow);
        Request = new IssueInvoiceRequest
        {
            Tenant = tenant, BillerID = biller, BilleeID = billee, InvoiceID = id,
            InvoiceItems = [new InvoiceItem { Invoice = Invoice.ParseID(id), ID = 1, Subject = "Synthetic service", UnitPriceCurrency = "NZD", UnitPriceCents = 10000, Quantity = 1, LineTotalCurrency = "NZD" }]
        };
        Biller = new Biller { PartitionKey = tenant, RowKey = biller, BusinessName = "Synthetic biller", Currency = "NZD", Culture = "en-NZ", TimeZone = "Pacific/Auckland", Country = "NZ", TaxRatePercentile = 1500, PaymentInstructions = "Bank transfer" };
        Billee = new Billee { Biller = biller, ID = billee, Name = "Synthetic customer", Currency = "NZD", Culture = "en-NZ", TimeZone = "Pacific/Auckland", Country = "NZ", Email = "recipient@example.invalid" };

        Profiles.Setup(p => p.RetrieveAsync(tenant, biller, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid __, bool ___, CancellationToken token) => { Seen(token); return MissingBiller ? null : Biller; });
        BilleeRepo.Setup(p => p.RetrieveAsync(Billee.BuildPartitionKey(biller), Billee.BuildRowKey(billee), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string __, IList<string>? ___, CancellationToken token) => { Seen(token); return MissingBillee ? null : Billee; });
        InvoiceRepo.Setup(p => p.RetrieveAsync(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(id), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string __, IList<string>? ___, CancellationToken token) => { Seen(token); return Stored == null ? null : Copy(Stored); });
        InvoiceRepo.Setup(p => p.CreateAsync(It.IsAny<IEnumerable<Invoice>>(), It.IsAny<bool>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Invoice> values, bool replace, DateTimeOffset? _, CancellationToken token) =>
            {
                Seen(token);
                if (FailInvoiceWrite || (Stored != null && !replace)) { throw new RequestFailedException(409, "Synthetic create conflict"); }
                return Persist(values.Single());
            });
        InvoiceRepo.Setup(p => p.UpdateAsync(It.IsAny<IEnumerable<Invoice>>(), true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Invoice> values, bool _, bool __, CancellationToken token) =>
            {
                Seen(token);
                BeforeInvoiceUpdate?.Invoke();
                Invoice input = values.Single();
                if (FailInvoiceWrite || Stored == null || input.ETag != Stored.ETag) { throw new RequestFailedException(412, "Synthetic stale ETag"); }
                return Persist(input);
            });
        ItemRepo.Setup(p => p.GetAsync(It.IsAny<string>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
            .Returns((string _, IList<string>? __, CancellationToken token) => { Seen(token); return Enumerate(Items.Select(Copy).ToArray()); });
        ItemRepo.Setup(p => p.DeleteAsync(It.IsAny<IEnumerable<InvoiceItem>>(), It.IsAny<bool>(), true, It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<InvoiceItem> values, bool _, bool __, CancellationToken token) =>
            {
                Seen(token);
                if (FailDelete) { throw new IOException("Synthetic item deletion failure"); }
                ItemWrites++;
                foreach (InvoiceItem item in values) { Items.RemoveAll(i => i.ID == item.ID); }
                return Task.CompletedTask;
            });
        ItemRepo.Setup(p => p.CreateAsync(It.IsAny<IEnumerable<InvoiceItem>>(), It.IsAny<bool>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<InvoiceItem> values, bool replace, DateTimeOffset? _, CancellationToken token) =>
            {
                Seen(token);
                ItemWrites++;
                foreach (InvoiceItem item in values)
                {
                    if (!replace && Items.Any(i => i.ID == item.ID)) { throw new RequestFailedException(409, "Synthetic duplicate item"); }
                    Items.RemoveAll(i => i.ID == item.ID);
                    Items.Add(Copy(item));
                    if (FailCreate) { throw new IOException("Synthetic partial item creation failure"); }
                }
                return values;
            });
    }

    internal IssueFlow CreateFlow()
    {
        IOptions<BillingOptions> options = Microsoft.Extensions.Options.Options.Create(new BillingOptions { GetInvoiceEndpoint = "https://invoice.example.invalid", InvoiceTokenSecretSalt = "synthetic-test-secret-not-for-production" });
        GenericDomainRepository<InvoiceDomain, Invoice> domains = new(
            () => new InvoiceDomain(options, new Lazy<IRepository<Invoice>>(() => InvoiceRepo.Object), []),
            new Lazy<IRepository<Invoice>>(() => InvoiceRepo.Object));
        Mock<IMessagingBroker<NotifyCommand>> broker = new(MockBehavior.Strict);
        broker.Setup(b => b.EnqueueAsync(It.IsAny<IEnumerable<MessagingEntry<NotifyCommand>>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<MessagingEntry<NotifyCommand>>, CancellationToken>((messages, token) => { Seen(token); Messages.AddRange(messages); })
            .Returns(Task.CompletedTask);
        return new IssueFlow(new UpsertFlow(domains, ItemRepo.Object, BilleeRepo.Object, Profiles.Object),
            new EmailFlow(domains, ItemRepo.Object, broker.Object, options, NullLogger<EmailFlow>.Instance));
    }

    internal Task RunAsync(Billee? embedded = null, CancellationToken cancellationToken = default) => CreateFlow().RunAsync(Request, embedded, cancellationToken);
    internal static T Copy<T>(T value) => JsonMarshaller.Unmarshall<T>(JsonMarshaller.Marshall(value!));

    private IEnumerable<Invoice> Persist(Invoice input)
    {
        InvoiceWrites++;
        Stored = Copy(input);
        Stored.ETag = (++version).ToString();
        Invoice result = Copy(Stored);
        if (DropWriteEtags) { result.ETag = null; }
        return [result];
    }

    private void Seen(CancellationToken token) { token.ThrowIfCancellationRequested(); Tokens.Add(token); }
    private static async IAsyncEnumerable<T> Enumerate<T>(IEnumerable<T> values)
    {
        foreach (T value in values) { yield return value; }
        await Task.CompletedTask;
    }
}
