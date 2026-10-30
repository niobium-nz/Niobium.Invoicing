using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Options;
using System.Text.Encodings.Web;

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
                Token = "any",
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
            html.Should().Contain(HtmlEncoder.Default.Encode(parameters["GRAND_TOTAL"]));
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
            (await act.Should().ThrowAsync<Niobium.ApplicationException>()).Which.ErrorCode.Should().Be(InternalError.Forbidden);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public async Task Invoice_without_saved_token_rejects_public_view(string? savedToken)
        {
            // Given an invoice not yet delivered (no token assigned)
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.Token = savedToken;
            InvoiceItem[] items = new[] { MakeItem(invoice.GetID(), 1, "Service", invoice.GrandTotalCurrency, 1000, 1, 1000) };
            Arranged arranged = ArrangeHtmlFlow(invoice, items);

            Func<Task<string>> act = () => arranged.Flow.RunAsync(issuer, invoice.GetID(), "anything", CancellationToken.None);

            (await act.Should().ThrowAsync<Niobium.ApplicationException>()).Which.ErrorCode.Should().Be(InternalError.Forbidden);
            arranged.ItemDomainRepo.Verify(r => r.GetAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
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
            string htmlA = await arrA.Flow.RunAsync(issuer, nz.GetID(), nz.Token!, CancellationToken.None);
            string htmlB = await arrB.Flow.RunAsync(issuer, cn.GetID(), cn.Token!, CancellationToken.None);

            // Then: amounts/dates differ and match domain parameters
            IReadOnlyDictionary<string, string> pA = await arrA.InvoiceDomain.BuildNotificationParametersAsync("t", CancellationToken.None);
            IReadOnlyDictionary<string, string> pB = await arrB.InvoiceDomain.BuildNotificationParametersAsync("t", CancellationToken.None);
            pA["GRAND_TOTAL"].Should().NotBe(pB["GRAND_TOTAL"]);
            htmlA.Should().Contain(HtmlEncoder.Default.Encode(pA["GRAND_TOTAL"]));
            htmlB.Should().Contain(HtmlEncoder.Default.Encode(pB["GRAND_TOTAL"]));
            htmlA.Should().Contain(HtmlEncoder.Default.Encode(pA["BILL_DATE"]));
            htmlB.Should().Contain(HtmlEncoder.Default.Encode(pB["BILL_DATE"]));
        }

        [TestMethod]
        public async Task No_items_renders_clean_invoice()
        {
            // Given an invoice with no line items
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            Arranged arranged = ArrangeHtmlFlow(invoice, Array.Empty<InvoiceItem>());

            // When
            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), invoice.Token!, CancellationToken.None);

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
            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), invoice.Token!, CancellationToken.None);

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
            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), invoice.Token!, CancellationToken.None);

            // Then
            html.Should().Contain("Billing Period:");
            html.Should().Contain("Payment is due by:");
        }

        [TestMethod]
        public async Task Invoice_and_line_text_is_encoded_only_for_html()
        {
            const string suppliedText = "<script>alert('example')</script> & \"quoted\"";
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.BillerName = suppliedText;
            invoice.BilleeName = suppliedText;
            invoice.BillerAddressLine1 = suppliedText;
            invoice.BilleeAddressLine1 = suppliedText;
            invoice.Terms = suppliedText;
            invoice.PaymentInstructions = suppliedText;
            invoice.Particulars = suppliedText;
            invoice.Reference = suppliedText;
            invoice.ContactName = suppliedText;
            InvoiceItem item = MakeItem(invoice.GetID(), 1, suppliedText, "USD", 1000, 1, 1000, suppliedText);
            Arranged arranged = ArrangeHtmlFlow(invoice, [item]);

            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), "any", CancellationToken.None);

            html.Should().NotContain(suppliedText);
            html.Should().Contain(HtmlEncoder.Default.Encode(suppliedText));
            html.Should().NotContain("{{SUBJECT}}");
            IReadOnlyDictionary<string, string> notification = await arranged.InvoiceDomain.BuildNotificationParametersAsync("any", CancellationToken.None);
            notification["BILLER_NAME"].Should().Be(suppliedText);
            notification["TERMS"].Should().Be(suppliedText);
            IReadOnlyDictionary<string, string> lineNotification = await arranged.ItemDomains[0].BuildNotificationParametersAsync();
            lineNotification["SUBJECT"].Should().Be(suppliedText);
            lineNotification["DESCRIPTION"].Should().Be(suppliedText);
        }

        [TestMethod]
        public async Task Values_containing_placeholders_remain_literal()
        {
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.BillerName = "Business {{REFERENCE}}";
            invoice.Reference = "Reference value";
            InvoiceItem item = MakeItem(invoice.GetID(), 1, "Item {{DESCRIPTION}}", "USD", 1000, 1, 1000, "Line detail");
            Arranged arranged = ArrangeHtmlFlow(invoice, [item]);

            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), "any", CancellationToken.None);

            html.Should().Contain("Business {{REFERENCE}}");
            html.Should().Contain("Item {{DESCRIPTION}}");
            html.Should().NotContain("Business Reference value");
            html.Should().NotContain("Item Line detail");
        }

        [TestMethod]
        [DataRow("javascript:alert(1)")]
        [DataRow("data:image/svg+xml,<svg onload='alert(1)'/>")]
        [DataRow("file:///example/logo.png")]
        [DataRow("//assets.example.invalid/logo.png")]
        [DataRow("https://assets.example.invalid/logo\n.png")]
        public async Task Disallowed_logo_urls_are_not_rendered(string logo)
        {
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.BillerLogo = logo;
            Arranged arranged = ArrangeHtmlFlow(invoice, []);

            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), "any", CancellationToken.None);

            html.Should().Contain("<img src=\"\" class=\"biller-logo\"");
        }

        [TestMethod]
        [DataRow("https://assets.example.invalid/logo.png?brand=A&size=2")]
        [DataRow("http://assets.example.invalid/logo.png?brand=A&size=2")]
        public async Task Allowed_logo_url_is_encoded_for_its_attribute(string logo)
        {
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.BillerLogo = logo;
            Arranged arranged = ArrangeHtmlFlow(invoice, []);

            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), "any", CancellationToken.None);

            html.Should().Contain($"<img src=\"{HtmlEncoder.Default.Encode(logo)}\" class=\"biller-logo\"");
            html.Should().Contain("<meta name=\"referrer\" content=\"no-referrer\" />");
        }

        [TestMethod]
        public async Task Contact_values_cannot_add_attributes_or_uri_query_parameters()
        {
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.ContactEmailAddress = "contact@example.invalid?bcc=other@example.invalid\" onclick=\"alert(1)";
            invoice.ContactPhoneNumber = "+64 9 123 4567\" onclick=\"alert(1)";
            Arranged arranged = ArrangeHtmlFlow(invoice, []);

            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), "any", CancellationToken.None);

            html.Should().Contain($"href=\"mailto:{Uri.EscapeDataString(invoice.ContactEmailAddress)}\"");
            html.Should().Contain($"href=\"tel:{Uri.EscapeDataString(invoice.ContactPhoneNumber)}\"");
            html.Should().NotContain("onclick=\"alert(1)");
            html.Should().Contain(HtmlEncoder.Default.Encode(invoice.ContactEmailAddress));
            html.Should().Contain(HtmlEncoder.Default.Encode(invoice.ContactPhoneNumber));
        }

        [TestMethod]
        public async Task Logo_value_cannot_break_out_of_its_quoted_attribute()
        {
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.BillerLogo = "https://assets.example.invalid/logo.png\" onerror=\"alert(1)";
            Arranged arranged = ArrangeHtmlFlow(invoice, []);

            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), invoice.Token!, CancellationToken.None);

            html.Should().NotContain("onerror=\"alert(1)");
            html.Should().MatchRegex("<img src=\"[^\"]*\" class=\"biller-logo\" />");
        }

        [TestMethod]
        [DataRow("0123456789abcdef")]
        [DataRow("0123456789ABCDEF")]
        public async Task Saved_legacy_link_remains_valid_without_recalculation_or_expiry(string suppliedToken)
        {
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.Token = "0123456789abcdef";
            invoice.Delivered = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            invoice.GrandTotalCents = 98765;
            Arranged arranged = ArrangeHtmlFlow(invoice, [], new BillingOptions
            {
                InvoiceTokenSecretSalt = string.Empty,
                GetInvoiceEndpoint = "https://invoice.example.invalid"
            });

            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), suppliedToken, CancellationToken.None);

            html.Should().Contain(invoice.BillerName);
            invoice.Token.Should().Be("0123456789abcdef");
            arranged.InvoiceRepo.Verify(r => r.CreateAsync(It.IsAny<IEnumerable<Invoice>>(), It.IsAny<bool>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
            arranged.InvoiceRepo.Verify(r => r.UpdateAsync(It.IsAny<IEnumerable<Invoice>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" ")]
        [DataRow("wrong")]
        [DataRow("0123456789abcdee")]
        [DataRow("0123456789abcdef ")]
        public async Task Saved_token_requires_an_exact_case_insensitive_match(string? suppliedToken)
        {
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.Token = "0123456789abcdef";
            Arranged arranged = ArrangeHtmlFlow(invoice, []);

            Func<Task<string>> act = () => arranged.Flow.RunAsync(issuer, invoice.GetID(), suppliedToken!, CancellationToken.None);

            (await act.Should().ThrowAsync<Niobium.ApplicationException>()).Which.ErrorCode.Should().Be(InternalError.Forbidden);
        }

        [TestMethod]
        public async Task New_saved_token_opens_invoice_and_other_token_does_not()
        {
            Guid issuer = Guid.NewGuid();
            Invoice invoice = MakeInvoice(issuer, Guid.NewGuid());
            invoice.Token = null;
            Arranged arranged = ArrangeHtmlFlow(invoice, []);
            string token = await arranged.InvoiceDomain.EnsureAccessTokenAsync("synthetic-test-secret-for-link-generation", CancellationToken.None);

            string html = await arranged.Flow.RunAsync(issuer, invoice.GetID(), token, CancellationToken.None);
            html.Should().Contain(invoice.BillerName);
            string modified = token[..^1] + (token[^1] == '0' ? "1" : "0");
            Func<Task<string>> act = () => arranged.Flow.RunAsync(issuer, invoice.GetID(), modified, CancellationToken.None);
            (await act.Should().ThrowAsync<Niobium.ApplicationException>()).Which.ErrorCode.Should().Be(InternalError.Forbidden);
        }
    }
}
