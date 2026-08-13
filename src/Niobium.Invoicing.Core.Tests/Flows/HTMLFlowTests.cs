using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Options;

namespace Niobium.Invoicing.Core.Tests.Flows
{
    [TestClass]
    public class HTMLFlowTests
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

        private sealed record Arranged(
            HTMLFlow Flow,
            InvoiceDomain InvoiceDomain,
            Invoice Invoice,
            InvoiceItemDomain[] ItemDomains,
            Mock<IDomainRepository<InvoiceDomain, Invoice>> InvoiceDomainRepo,
            Mock<IDomainRepository<InvoiceItemDomain, InvoiceItem>> ItemDomainRepo,
            Mock<IRepository<Invoice>> InvoiceRepo,
            Mock<IRepository<InvoiceItem>>[] ItemRepos,
            Mock<ILogger<HTMLFlow>> Logger
        );

        // Factory to create a real domain with mocked infra and return all collaborators
        private Arranged ArrangeHtmlFlow(
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
            Mock<IDomainRepository<InvoiceDomain, Invoice>> invoiceDomainRepo = new(MockBehavior.Strict);
            Mock<IDomainRepository<InvoiceItemDomain, InvoiceItem>> itemDomainRepo = new(MockBehavior.Strict);
            Mock<ILogger<HTMLFlow>> logger = new();

            // Real invoice domain with mocked infra
            InvoiceDomain invDomain = new(options, new Lazy<IRepository<Invoice>>(() => invoiceRepo.Object), Enumerable.Empty<IDomainEventHandler<IDomain<Invoice>>>());
            string pk = Invoice.BuildPartitionKey(invoice.Biller);
            string rk = Invoice.BuildRowKey(invoice.GetID());
            invDomain.Initialize(pk, rk);

            // invoice repository behavior for domain load
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
            invoiceDomainRepo
                .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((string p, string r, bool _, CancellationToken __) =>
                {
                    invDomain.Initialize(p, r);
                    return Task.FromResult(invDomain);
                });

            // Build item domains, each with its own item repository
            List<InvoiceItemDomain> itemDomains = new();
            List<Mock<IRepository<InvoiceItem>>> itemRepos = new();
            string itemsPk = InvoiceItem.BuildPartitionKey(invoice.GetID());
            foreach (InvoiceItem it in items)
            {
                Mock<IRepository<InvoiceItem>> itemRepo = new(MockBehavior.Strict);
                itemRepo
                    .Setup(r => r.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                itemRepo
                    .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(it);

                InvoiceItemDomain itemDomain = new(new Lazy<IRepository<InvoiceItem>>(() => itemRepo.Object), Enumerable.Empty<IDomainEventHandler<IDomain<InvoiceItem>>>());
                string itemPk = itemsPk;
                string itemRk = InvoiceItem.BuildRowKey((int)it.ID);
                itemDomain.Initialize(itemPk, itemRk);

                itemDomains.Add(itemDomain);
                itemRepos.Add(itemRepo);
            }

            // item domain repo returns IAsyncEnumerable of the real item domains for given partition
            itemDomainRepo
                .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((string part, bool _, CancellationToken __) =>
                {
                    return part == itemsPk ? YieldAsync(itemDomains.ToArray()) : YieldAsync(Array.Empty<InvoiceItemDomain>());
                });

            HTMLFlow flow = new(invoiceDomainRepo.Object, itemDomainRepo.Object);
            return new Arranged(flow, invDomain, invoice, itemDomains.ToArray(), invoiceDomainRepo, itemDomainRepo, invoiceRepo, itemRepos.ToArray(), logger);
        }

        private static Invoice MakeInvoice(Guid biller, Guid billee, string currency = "USD")
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
                TaxRatePercentile = 1500,
                Terms = "Payment due within 7 days.",
                PaymentInstructions = "ACME LTD 01-2345-6789012-00",
            };
            return invoice;
        }

        private static InvoiceItem MakeItem(long invoiceId, int id, string subject, string currency, long unitCents, int qty, long lineTotalCents, string? description = null)
        {
            return new InvoiceItem
            {
                Invoice = Invoice.ParseID(invoiceId),
                ID = id,
                Subject = subject,
                Description = description,
                UnitPriceCurrency = currency,
                UnitPriceCents = unitCents,
                Quantity = qty,
                LineTotalCurrency = currency,
                LineTotalCents = lineTotalCents,
            };
        }

        [TestMethod]
        public async Task Invoice_with_items_renders_fully()
        {
            // Given a normal invoice with two line items
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.SubtotalCents = 3000;
            invoice.TaxCents = 450;
            invoice.GrandTotalCents = 3450;
            invoice.DueBy = DateTimeOffset.UtcNow.AddDays(7);
            InvoiceItem[] items = new[]
            {
                MakeItem(invoice.GetID(), 1, "Consulting", invoice.GrandTotalCurrency, 1500, 1, 1500, "One hour consult"),
                MakeItem(invoice.GetID(), 2, "Development", invoice.GrandTotalCurrency, 1500, 1, 1500, "One hour dev work"),
            };
            Arranged arranged = ArrangeHtmlFlow(invoice, items);

            // When the HTML is generated
            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), token: "any", CancellationToken.None);

            // Then the invoice shows parties, totals, and item details
            html.Should().Contain(invoice.BillerName);
            html.Should().Contain(invoice.BilleeName);
            html.Should().Contain("TOTAL AMOUNT DUE");
            foreach (InvoiceItem? it in items)
            {
                html.Should().Contain(it.Subject);
                if (!string.IsNullOrWhiteSpace(it.Description))
                {
                    html.Should().Contain(it.Description);
                }
            }

            // And the shown total matches the domain's formatted parameters
            IReadOnlyDictionary<string, string> parameters = await arranged.InvoiceDomain.BuildNotificationParametersAsync("any", CancellationToken.None);
            html.Should().Contain(parameters["GRAND_TOTAL"]);
        }

        [TestMethod]
        public async Task Wrong_token_is_rejected_after_delivery()
        {
            // Given an already delivered invoice with a token
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.Token = "expected-token";
            invoice.Delivered = DateTimeOffset.UtcNow;
            InvoiceItem[] items = new[] { MakeItem(invoice.GetID(), 1, "Service", invoice.GrandTotalCurrency, 1000, 1, 1000) };
            Arranged arranged = ArrangeHtmlFlow(invoice, items);

            // When/Then rendering with wrong token is forbidden
            Func<Task<string>> act = async () => await arranged.Flow.RunAsync(issuer, invoice.GetID(), token: "wrong-token", CancellationToken.None);
            await act.Should().ThrowAsync<Exception>();
        }

        [TestMethod]
        public async Task Preview_before_delivery_succeeds_without_token()
        {
            // Given an invoice not yet delivered (no token assigned)
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            InvoiceItem[] items = new[] { MakeItem(invoice.GetID(), 1, "Service", invoice.GrandTotalCurrency, 1000, 1, 1000) };
            Arranged arranged = ArrangeHtmlFlow(invoice, items);

            // When rendering with any token
            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), token: "anything", CancellationToken.None);

            // Then HTML renders successfully with key business details
            html.Should().Contain(invoice.BillerName);
            html.Should().Contain(invoice.BilleeName);
        }

        [TestMethod]
        public async Task Localization_respects_culture_timezone_and_currency()
        {
            // Given two invoices with different culture/timezone/currency
            Guid issuer = Guid.NewGuid();
            Invoice nz = MakeInvoice(issuer, Guid.NewGuid(), currency: "USD");
            nz.Culture = "en-NZ";
            nz.TimeZone = "Pacific/Auckland";
            nz.SubtotalCents = 1000; nz.TaxCents = 0; nz.GrandTotalCents = 1000;
            Invoice cn = MakeInvoice(issuer, Guid.NewGuid(), currency: "CNY");
            cn.Culture = "zh-CN";
            cn.TimeZone = "Asia/Shanghai";
            cn.SubtotalCents = 1000; cn.TaxCents = 0; cn.GrandTotalCents = 1000;

            Arranged arrA = ArrangeHtmlFlow(nz, new[] { MakeItem(nz.GetID(), 1, "One", nz.GrandTotalCurrency, 1000, 1, 1000) });
            Arranged arrB = ArrangeHtmlFlow(cn, new[] { MakeItem(cn.GetID(), 1, "Un", cn.GrandTotalCurrency, 1000, 1, 1000) });

            // When
            string htmlA = await arrA.Flow.RunAsync(issuer, nz.GetID(), token: "t", CancellationToken.None);
            string htmlB = await arrB.Flow.RunAsync(issuer, cn.GetID(), token: "t", CancellationToken.None);

            // Then: amounts/dates differ and match domain parameters
            IReadOnlyDictionary<string, string> pA = await arrA.InvoiceDomain.BuildNotificationParametersAsync("t", CancellationToken.None);
            IReadOnlyDictionary<string, string> pB = await arrB.InvoiceDomain.BuildNotificationParametersAsync("t", CancellationToken.None);
            pA["GRAND_TOTAL"].Should().NotBe(pB["GRAND_TOTAL"]);
            htmlA.Should().Contain(pA["GRAND_TOTAL"]);
            htmlB.Should().Contain(pB["GRAND_TOTAL"]);
            htmlA.Should().Contain(pA["BILL_DATE"]);
            htmlB.Should().Contain(pB["BILL_DATE"]);
        }

        [TestMethod]
        public async Task No_items_renders_clean_invoice()
        {
            // Given an invoice with no line items
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            Arranged arranged = ArrangeHtmlFlow(invoice, Array.Empty<InvoiceItem>());

            // When
            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), token: "z", CancellationToken.None);

            // Then the invoice still renders key business info
            html.Should().Contain(invoice.BillerName);
        }

        [TestMethod]
        public async Task Line_item_formatting_is_correct()
        {
            // Given a single line item with known values
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.SubtotalCents = 2000; invoice.TaxCents = 0; invoice.GrandTotalCents = 2000;
            InvoiceItem item = MakeItem(invoice.GetID(), 1, "Sticker", invoice.GrandTotalCurrency, 1000, 2, 2000, "Vinyl sticker pack");
            Arranged arranged = ArrangeHtmlFlow(invoice, new[] { item });

            // When
            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), token: "x", CancellationToken.None);

            // Then the row shows subject, description, quantity
            html.Should().Contain(item.Subject);
            html.Should().Contain(item.Description!);
            html.Should().MatchRegex(@".*>2<.*"); // quantity appears
        }

        [TestMethod]
        public async Task Billing_period_and_due_date_are_shown()
        {
            // Given a monthly invoice for a specific period and a due date
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.InvoiceCycle = (int)InvoiceCycle.Range;
            invoice.BillingPeriodStartDay = DateTimeOffset.UtcNow.AddDays(-30);
            invoice.BillingPeriodEndDay = DateTimeOffset.UtcNow.AddDays(-1);
            invoice.DueBy = DateTimeOffset.UtcNow.AddDays(5);
            Arranged arranged = ArrangeHtmlFlow(invoice, new[] { MakeItem(invoice.GetID(), 1, "Hosting", invoice.GrandTotalCurrency, 1000, 1, 1000) });

            // When
            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), token: "y", CancellationToken.None);

            // Then
            html.Should().Contain("Billing Period:");
            html.Should().Contain("Payment is due by:");
        }
    }
}
