using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Options;
using Niobium.Messaging;
using Niobium.Notification;

namespace Niobium.Invoicing.Core.Tests.Flows
{
    [TestClass]
    public class EmailFlowTests
    {
        // Helper: yield an async stream from params
        private static async IAsyncEnumerable<T> YieldAsync<T>(params T[] items)
        {
            foreach (T? i in items)
            {
                yield return i;
                await Task.Yield();
            }
        }

        // Factory to create a real domain with mocked infra and return all collaborators
        private static (EmailFlow flow, InvoiceDomain domain, Invoice entity,
            Mock<IDomainRepository<InvoiceDomain, Invoice>> domainRepo,
            Mock<IRepository<Invoice>> invoiceRepo,
            Mock<IRepository<InvoiceItem>> itemRepo,
            Mock<IMessagingBroker<NotifyCommand>> broker,
            Mock<ILogger<EmailFlow>> logger) ArrangeEmailFlow(
                Invoice invoice,
                IEnumerable<InvoiceItem> items,
                BillingOptions? billingOptions = null)
        {
            IOptions<BillingOptions> options = Microsoft.Extensions.Options.Options.Create(billingOptions ?? new BillingOptions
            {
                InvoiceTokenSecretSalt = "super-secret-salt-1234567890",
                GetInvoiceEndpoint = "https://billing.example.com/invoices"
            });

            Mock<IRepository<Invoice>> invoiceRepo = new(MockBehavior.Strict);
            Mock<IDomainRepository<InvoiceDomain, Invoice>> domainRepo = new(MockBehavior.Strict);
            Mock<IRepository<InvoiceItem>> itemRepo = new(MockBehavior.Strict);
            Mock<IMessagingBroker<NotifyCommand>> broker = new(MockBehavior.Loose);
            Mock<ILogger<EmailFlow>> logger = new();

            // Real domain with mocked infra
            InvoiceDomain domain = new(options, new Lazy<IRepository<Invoice>>(() => invoiceRepo.Object), Enumerable.Empty<IDomainEventHandler<IDomain<Invoice>>>());

            // Initialize keys so GenericDomain can load using mocked repo
            string pk = Invoice.BuildPartitionKey(invoice.Biller);
            string rk = Invoice.BuildRowKey(invoice.GetID());
            domain.Initialize(pk, rk);

            // invoiceRepo is used by GenericDomain base for load/save
            invoiceRepo
                .Setup(r => r.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            invoiceRepo
                .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(invoice);
            invoiceRepo
                .Setup(r => r.UpdateAsync(It.IsAny<IEnumerable<Invoice>>(), true, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<Invoice> e, bool _, bool __, CancellationToken ___) => e);
            invoiceRepo
                .Setup(r => r.CreateAsync(It.IsAny<IEnumerable<Invoice>>(), false, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<Invoice> e, bool _, DateTimeOffset? __, CancellationToken ___) => e);

            // Domain repository returns the real domain for GetAsync and ensures it's initialized
            domainRepo
                .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((string p, string r, bool _, CancellationToken __) =>
                {
                    domain.Initialize(p, r);
                    return Task.FromResult(domain);
                });

            // Items repo returns provided items for the invoice
            itemRepo
                .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
                .Returns(YieldAsync(items.ToArray()));

            EmailFlow flow = new(domainRepo.Object, itemRepo.Object, broker.Object, options, logger.Object);
            return (flow, domain, invoice, domainRepo, invoiceRepo, itemRepo, broker, logger);
        }

        private static Invoice MakeInvoice(Guid biller, Guid billee, string? recipientEmail = null, string currency = "USD")
        {
            Invoice invoice = new()
            {
                Tenant = biller,
                Biller = biller,
                Created = Invoice.ParseID(20250101010101),
                BillerName = "Acme Ltd",
                Billee = billee,
                BilleeName = "John Customer",
                GrandTotalCurrency = currency,
                SubtotalCurrency = currency,
                TaxCurrency = currency,
                TimeZone = "Pacific/Auckland",
                Culture = "en-NZ",
                RecipientEmail = recipientEmail,
                TaxRatePercentile = 1500,
            };
            return invoice;
        }

        private static InvoiceItem MakeItem(long invoiceId, int id, string subject, string currency, long unitCents, int qty, long lineTotalCents)
        {
            return new InvoiceItem
            {
                Invoice = Invoice.ParseID(invoiceId),
                ID = id,
                Subject = subject,
                UnitPriceCurrency = currency,
                UnitPriceCents = unitCents,
                Quantity = qty,
                LineTotalCurrency = currency,
                LineTotalCents = lineTotalCents,
            };
        }

        [TestMethod]
        public async Task Customer_without_email_is_not_contacted()
        {
            // Given an invoice without recipient email
            Guid issuer = Guid.NewGuid();
            Invoice entity = MakeInvoice(issuer, Guid.NewGuid(), recipientEmail: null);
            InvoiceItem[] items = [MakeItem(entity.GetID(), 1, "Service", entity.GrandTotalCurrency, 1000, 1, 1000)];
            (EmailFlow flow, InvoiceDomain domain, Invoice entity, Mock<IDomainRepository<InvoiceDomain, Invoice>> domainRepo, Mock<IRepository<Invoice>> invoiceRepo, Mock<IRepository<InvoiceItem>> itemRepo, Mock<IMessagingBroker<NotifyCommand>> broker, Mock<ILogger<EmailFlow>> logger) arranged = ArrangeEmailFlow(entity, items);

            // When the email flow runs
            await arranged.flow.RunAsync(issuer, entity.GetID(), CancellationToken.None);

            // Then a warning is logged and nothing is sent or persisted
            arranged.logger.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Warning),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("has no recipient email")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
            arranged.invoiceRepo.Verify(r => r.UpdateAsync(It.IsAny<IEnumerable<Invoice>>(), true, false, It.IsAny<CancellationToken>()), Times.Never);
            entity.Token.Should().BeNull();
            entity.Delivered.Should().BeNull();
        }

        [TestMethod]
        public async Task Customer_receives_secure_invoice_link_and_invoice_marked_delivered()
        {
            // Given a normal invoice with a contact email and items
            Guid issuer = Guid.NewGuid();
            Invoice entity = MakeInvoice(issuer, Guid.NewGuid(), recipientEmail: "john@example.com");
            entity.GrandTotalCents = 2500; // $25.00
            InvoiceItem[] items =
            [
                MakeItem(entity.GetID(), 1, "Consulting", entity.GrandTotalCurrency, 2500, 1, 2500)
            ];

            (EmailFlow flow, InvoiceDomain domain, Invoice entity, Mock<IDomainRepository<InvoiceDomain, Invoice>> domainRepo, Mock<IRepository<Invoice>> invoiceRepo, Mock<IRepository<InvoiceItem>> itemRepo, Mock<IMessagingBroker<NotifyCommand>> broker, Mock<ILogger<EmailFlow>> logger) arranged = ArrangeEmailFlow(entity, items);

            // When
            await arranged.flow.RunAsync(issuer, entity.GetID(), CancellationToken.None);

            // Then invoice is persisted with token and delivered set (create or update depending on entity state)
            arranged.invoiceRepo.Verify(r => r.CreateAsync(It.Is<IEnumerable<Invoice>>(e => e.Any(x => x.Token != null && x.Delivered != null)), false, null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [TestMethod]
        public async Task Secure_link_token_is_present_and_well_formed()
        {
            // Given
            Guid issuer = Guid.NewGuid();
            Invoice entity = MakeInvoice(issuer, Guid.NewGuid(), recipientEmail: "alice@example.com");
            entity.GrandTotalCents = 12345;
            InvoiceItem[] items =
            [
                MakeItem(entity.GetID(), 1, "Hosting", entity.GrandTotalCurrency, 1000, 2, 2000),
                MakeItem(entity.GetID(), 2, "Support", entity.GrandTotalCurrency, 500, 1, 500),
            ];

            (EmailFlow flow, InvoiceDomain domain, Invoice entity, Mock<IDomainRepository<InvoiceDomain, Invoice>> domainRepo, Mock<IRepository<Invoice>> invoiceRepo, Mock<IRepository<InvoiceItem>> itemRepo, Mock<IMessagingBroker<NotifyCommand>> broker, Mock<ILogger<EmailFlow>> logger) arranged = ArrangeEmailFlow(entity, items);

            // When
            await arranged.flow.RunAsync(issuer, entity.GetID(), CancellationToken.None);

            // Then token exists and looks like a SHA256-derived hex of length 16
            entity.Token.Should().NotBeNullOrWhiteSpace();
            entity.Token!.Length.Should().Be(16);
            entity.Token!.All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f')).Should().BeTrue();
        }

        [TestMethod]
        public async Task Notification_includes_key_business_facts()
        {
            // Given
            Guid issuer = Guid.NewGuid();
            Invoice entity = MakeInvoice(issuer, Guid.NewGuid(), recipientEmail: "biz@example.com");
            entity.BillerBusinessID = "NZBN-123";
            entity.DueBy = DateTimeOffset.UtcNow.AddDays(5);
            entity.GrandTotalCents = 5050; // $50.50
            InvoiceItem[] items = [MakeItem(entity.GetID(), 1, "Service", entity.GrandTotalCurrency, 5050, 1, 5050)];

            (EmailFlow flow, InvoiceDomain domain, Invoice entity, Mock<IDomainRepository<InvoiceDomain, Invoice>> domainRepo, Mock<IRepository<Invoice>> invoiceRepo, Mock<IRepository<InvoiceItem>> itemRepo, Mock<IMessagingBroker<NotifyCommand>> broker, Mock<ILogger<EmailFlow>> logger) arranged = ArrangeEmailFlow(entity, items);

            // When
            await arranged.flow.RunAsync(issuer, entity.GetID(), CancellationToken.None);

            // Then business fields exist in parameters from the real domain
            IReadOnlyDictionary<string, string> p = await arranged.domain.BuildNotificationParametersAsync(entity.Token!, CancellationToken.None);
            p.Should().ContainKeys("BILL_DATE", "BILLER_NAME", "BILLEE_NAME", "GRAND_TOTAL", "DUE", "INVOICE_URL");
            p["BILLER_NAME"].Should().Be(entity.BillerName);
            p["BILLEE_NAME"].Should().Be(entity.BilleeName);
            p["GRAND_TOTAL"].Should().NotBeNullOrEmpty();
            p["INVOICE_URL"].Should().StartWith("https://billing.example.com/invoices/");
        }

        [TestMethod]
        public async Task Localization_respects_culture_and_timezone()
        {
            // Given two invoices differing by culture/timezone
            Guid issuer = Guid.NewGuid();
            Invoice enNz = MakeInvoice(issuer, Guid.NewGuid(), recipientEmail: "a@example.com");
            enNz.Culture = "en-NZ";
            enNz.TimeZone = "Pacific/Auckland";
            enNz.GrandTotalCents = 1000;
            Invoice frFr = MakeInvoice(issuer, Guid.NewGuid(), recipientEmail: "b@example.com");
            frFr.GrandTotalCurrency = "CNY";
            frFr.SubtotalCurrency = "CNY";
            frFr.TaxCurrency = "CNY";
            frFr.Culture = "zh-CN";
            frFr.TimeZone = "Asia/Shanghai";
            frFr.GrandTotalCents = 1000;

            (EmailFlow flow, InvoiceDomain domain, Invoice entity, Mock<IDomainRepository<InvoiceDomain, Invoice>> domainRepo, Mock<IRepository<Invoice>> invoiceRepo, Mock<IRepository<InvoiceItem>> itemRepo, Mock<IMessagingBroker<NotifyCommand>> broker, Mock<ILogger<EmailFlow>> logger) = ArrangeEmailFlow(enNz, new[] { MakeItem(enNz.GetID(), 1, "One", enNz.GrandTotalCurrency, 1000, 1, 1000) });
            (EmailFlow flow, InvoiceDomain domain, Invoice entity, Mock<IDomainRepository<InvoiceDomain, Invoice>> domainRepo, Mock<IRepository<Invoice>> invoiceRepo, Mock<IRepository<InvoiceItem>> itemRepo, Mock<IMessagingBroker<NotifyCommand>> broker, Mock<ILogger<EmailFlow>> logger) arrB = ArrangeEmailFlow(frFr, new[] { MakeItem(frFr.GetID(), 1, "Un", frFr.GrandTotalCurrency, 1000, 1, 1000) });

            // When
            await flow.RunAsync(issuer, enNz.GetID(), CancellationToken.None);
            await arrB.flow.RunAsync(issuer, frFr.GetID(), CancellationToken.None);

            // Then: formatted dates differ by culture/timezone
            IReadOnlyDictionary<string, string> pA = await domain.BuildNotificationParametersAsync(enNz.Token!, CancellationToken.None);
            IReadOnlyDictionary<string, string> pB = await arrB.domain.BuildNotificationParametersAsync(frFr.Token!, CancellationToken.None);
            pA["GRAND_TOTAL"].Should().NotBe(pB["GRAND_TOTAL"]);
            pA["BILL_DATE"].Should().NotBe(pB["BILL_DATE"]);
        }

        [TestMethod]
        public async Task Simple_invoice_still_notifies_cleanly()
        {
            // Given a single low-value item and zero tax
            Guid issuer = Guid.NewGuid();
            Invoice entity = MakeInvoice(issuer, Guid.NewGuid(), recipientEmail: "simple@example.com");
            entity.SubtotalCents = 50;
            entity.TaxCents = 0;
            entity.GrandTotalCents = 50;
            InvoiceItem[] items = [MakeItem(entity.GetID(), 1, "Sticker", entity.GrandTotalCurrency, 50, 1, 50)];

            (EmailFlow flow, InvoiceDomain domain, Invoice entity, Mock<IDomainRepository<InvoiceDomain, Invoice>> domainRepo, Mock<IRepository<Invoice>> invoiceRepo, Mock<IRepository<InvoiceItem>> itemRepo, Mock<IMessagingBroker<NotifyCommand>> broker, Mock<ILogger<EmailFlow>> logger) arranged = ArrangeEmailFlow(entity, items);

            // When
            await arranged.flow.RunAsync(issuer, entity.GetID(), CancellationToken.None);

            // Then
            IReadOnlyDictionary<string, string> p = await arranged.domain.BuildNotificationParametersAsync(entity.Token!, CancellationToken.None);
            p["GRAND_TOTAL"].Should().NotBeNullOrEmpty();
            p.Should().ContainKey("INVOICE_URL");
        }

        [TestMethod]
        public async Task Resent_after_items_change_generates_new_token()
        {
            // Given an invoice sent once, then items change
            Guid issuer = Guid.NewGuid();
            Invoice entity = MakeInvoice(issuer, Guid.NewGuid(), recipientEmail: "again@example.com");
            entity.GrandTotalCents = 2000;
            InvoiceItem[] items1 = [MakeItem(entity.GetID(), 1, "A", entity.GrandTotalCurrency, 1000, 2, 2000)];
            InvoiceItem[] items2 = [MakeItem(entity.GetID(), 1, "A", entity.GrandTotalCurrency, 1500, 1, 1500)];

            (EmailFlow flow, InvoiceDomain domain, Invoice entity, Mock<IDomainRepository<InvoiceDomain, Invoice>> domainRepo, Mock<IRepository<Invoice>> invoiceRepo, Mock<IRepository<InvoiceItem>> itemRepo, Mock<IMessagingBroker<NotifyCommand>> broker, Mock<ILogger<EmailFlow>> logger) arranged = ArrangeEmailFlow(entity, items1);

            await arranged.flow.RunAsync(issuer, entity.GetID(), CancellationToken.None);
            string firstToken = entity.Token!;

            // Rewire items repo to return changed items
            arranged.itemRepo
                .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
                .Returns(YieldAsync(items2.ToArray()));

            // When sending again
            await arranged.flow.RunAsync(issuer, entity.GetID(), CancellationToken.None);

            // Then token changes and persistence reflects new token
            string secondToken = entity.Token!;
            secondToken.Should().NotBe(firstToken);
            arranged.invoiceRepo.Verify(r => r.CreateAsync(It.Is<IEnumerable<Invoice>>(e => e.Any(x => x.Token == secondToken)), false, null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }
    }
}
