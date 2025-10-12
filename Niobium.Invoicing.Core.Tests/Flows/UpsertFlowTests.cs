using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Niobium.Finance;
using Niobium.Invoicing;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Options;
using Niobium.Profile;

namespace Niobium.Invoicing.Core.Tests.Flows
{
    [TestClass]
    public class UpsertFlowTests
    {
        // Helper: yield an async stream from params (copied pattern from existing tests)
        private static async IAsyncEnumerable<T> YieldAsync<T>(params T[] items)
        {
            foreach (T? i in items)
            {
                yield return i;
                await Task.Yield();
            }
        }

        private static BillingOptions DefaultBillingOptions => new()
        {
            InvoiceTokenSecretSalt = "super-secret-salt-1234567890",
            GetInvoiceEndpoint = "https://billing.example.com/invoices",
        };

        private static IssueInvoiceRequest MakeRequest(Guid tenant, Guid billerId, Guid billeeId, long invoiceId, IEnumerable<InvoiceItem> items,
            string? terms = "  NET 7 DAYS  ", string? paymentInstructions = "  Pay by bank transfer  ",
            int invoiceCycle = (int)InvoiceCycle.Monthly, DateTimeOffset? start = null, DateTimeOffset? end = null, DateTimeOffset? dueBy = null,
            long settledCents = 0)
        {
            return new IssueInvoiceRequest
            {
                Tenant = tenant,
                BillerID = billerId,
                BilleeID = billeeId,
                InvoiceID = invoiceId,
                InvoiceItems = items.ToList(),
                Terms = terms,
                PaymentInstructions = paymentInstructions,
                InvoiceCycle = invoiceCycle,
                BillingPeriodStartDay = start,
                BillingPeriodEndDay = end,
                DueBy = dueBy,
                Settled = new Amount { Cents = settledCents, Currency = items.First().LineTotalCurrency },
                NotifyBillee = false,
            };
        }

        private static Billee MakeBillee(Guid biller, string? email = "john@example.com", string currency = "USD")
        {
            return new Billee
            {
                Biller = biller,
                ID = Guid.NewGuid(),
                Name = "John Customer",
                Email = email,
                Country = "NZ",
                State = "AUK",
                City = "Auckland",
                Culture = "en-NZ",
                Currency = currency,
                TimeZone = "Pacific/Auckland",
            };
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

        private sealed record Arranged(
            UpsertFlow Flow,
            InvoiceDomain Domain,
            Mock<IDomainRepository<InvoiceDomain, Invoice>> InvoiceDomainRepo,
            Mock<IRepository<Invoice>> InvoiceRepo,
            Mock<IRepository<InvoiceItem>> ItemRepo,
            Mock<IRepository<Billee>> BilleeRepo,
            Mock<IProfileService<Biller>> ProfileService,
            Invoice BuiltEntity
        );

        // Build a real domain returned by BuildAsync while keeping infra mocked
        private static Arranged ArrangeUpsertFlow(
            Guid tenant,
            Guid billerId,
            Guid billeeId,
            long invoiceId,
            IEnumerable<InvoiceItem> requestItems,
            IEnumerable<InvoiceItem>? existingItems = null,
            BillingOptions? billingOptions = null)
        {
            IOptions<BillingOptions> options = Microsoft.Extensions.Options.Options.Create(billingOptions ?? DefaultBillingOptions);

            var invoiceRepo = new Mock<IRepository<Invoice>>(MockBehavior.Strict);
            var invoiceDomainRepo = new Mock<IDomainRepository<InvoiceDomain, Invoice>>(MockBehavior.Strict);
            var itemRepo = new Mock<IRepository<InvoiceItem>>(MockBehavior.Strict);
            var billeeRepo = new Mock<IRepository<Billee>>(MockBehavior.Strict);
            var profileService = new Mock<IProfileService<Biller>>(MockBehavior.Strict);

            // Real domain with mocked infra
            var domain = new InvoiceDomain(options, new Lazy<IRepository<Invoice>>(() => invoiceRepo.Object), Enumerable.Empty<IDomainEventHandler<IDomain<Invoice>>>());

            // A mocked Biller from Niobium.Profile (avoid needing to know required props)
            var biller = new Mock<Biller>(MockBehavior.Loose).Object;

            profileService
                .Setup(s => s.RetrieveAsync(tenant, billerId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(biller);

            // Billee retrieval when not supplied
            billeeRepo
                .Setup(r => r.RetrieveAsync(Billee.BuildPartitionKey(billerId), Billee.BuildRowKey(billeeId), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeBillee(billerId));

            // BuildAsync returns the real domain initialized for the entity being created
            Invoice? builtEntity = null;
            invoiceDomainRepo
                .Setup(r => r.BuildAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
                .Returns((Invoice inv, CancellationToken _) =>
                {
                    builtEntity = inv;
                    // Initialize domain with the entity keys and wire repo for load/save
                    string pk = Invoice.BuildPartitionKey(inv.Biller);
                    string rk = Invoice.BuildRowKey(inv.GetID());
                    domain.Initialize(pk, rk);

                    invoiceRepo
                        .Setup(x => x.ExistsAsync(pk, rk, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(true);
                    invoiceRepo
                        .Setup(x => x.RetrieveAsync(pk, rk, It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(inv);
                    invoiceRepo
                        .Setup(x => x.UpdateAsync(It.IsAny<IEnumerable<Invoice>>(), true, false, It.IsAny<CancellationToken>()))
                        .ReturnsAsync((IEnumerable<Invoice> e, bool _, bool __, CancellationToken ___) => e);
                    invoiceRepo
                        .Setup(x => x.CreateAsync(It.IsAny<IEnumerable<Invoice>>(), It.IsAny<bool>(), null, It.IsAny<CancellationToken>()))
                        .ReturnsAsync((IEnumerable<Invoice> e, bool _, DateTimeOffset? __, CancellationToken ___) => e);

                    return Task.FromResult(domain);
                });

            // Existing items in store (if any)
            if (existingItems != null)
            {
                itemRepo
                    .Setup(r => r.GetAsync(InvoiceItem.BuildPartitionKey(invoiceId), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
                    .Returns(YieldAsync(existingItems.ToArray()));
            }
            else
            {
                itemRepo
                    .Setup(r => r.GetAsync(InvoiceItem.BuildPartitionKey(invoiceId), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
                    .Returns(YieldAsync(Array.Empty<InvoiceItem>()));
            }

            // Deletions and creations
            itemRepo
                .Setup(r => r.DeleteAsync(It.IsAny<IEnumerable<InvoiceItem>>(), true, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            itemRepo
                .Setup(r => r.CreateAsync(It.IsAny<IEnumerable<InvoiceItem>>(), It.IsAny<bool>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<InvoiceItem> e, bool _, DateTimeOffset? __, CancellationToken ___) => e);

            var flow = new UpsertFlow(invoiceDomainRepo.Object, itemRepo.Object, billeeRepo.Object, profileService.Object);

            return new Arranged(flow, domain, invoiceDomainRepo, invoiceRepo, itemRepo, billeeRepo, profileService, builtEntity!);
        }

        [TestMethod]
        public async Task New_invoice_is_issued_and_items_are_persisted()
        {
            // Given a new invoice with 2 items
            Guid tenant = Guid.NewGuid();
            Guid billerId = Guid.NewGuid();
            Guid billeeId = Guid.NewGuid();
            long invoiceId = 20250101010101;
            var items = new[]
            {
                // Provide wrong line totals on purpose ¨C domain should recalculate
                MakeItem(invoiceId, 1, "Consulting", "USD", 1500, 2, lineTotalCents: 0),
                MakeItem(invoiceId, 2, "Hosting", "USD", 500, 1, lineTotalCents: 999),
            };
            var request = MakeRequest(tenant, billerId, billeeId, invoiceId, items, dueBy: DateTimeOffset.UtcNow.AddDays(14), settledCents: 0);

            Arranged arr = ArrangeUpsertFlow(tenant, billerId, billeeId, invoiceId, items);
            var billee = MakeBillee(billerId);

            // When
            await arr.Flow.RunAsync(request, billee, CancellationToken.None);

            // Then invoice saved with correct totals and fields set
            arr.InvoiceRepo.Verify(r => r.CreateAsync(It.Is<IEnumerable<Invoice>>(e =>
                e.Any(inv => inv.GrandTotalCents == 3500 && inv.SubtotalCents == 3500 && inv.TaxCents >= 0 && inv.Terms == "NET 7 DAYS" && inv.PaymentInstructions == "Pay by bank transfer")),
                true, null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);

            // And items created with corrected line totals
            arr.ItemRepo.Verify(r => r.CreateAsync(It.Is<IEnumerable<InvoiceItem>>(it =>
                it.Any(x => x.ID == 1 && x.LineTotalCents == 3000) &&
                it.Any(x => x.ID == 2 && x.LineTotalCents == 500)
            ), true, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);

            // No deletions because none existed
            arr.ItemRepo.Verify(r => r.DeleteAsync(It.IsAny<IEnumerable<InvoiceItem>>(), true, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestMethod]
        public async Task Existing_items_are_replaced_when_reissuing()
        {
            // Given existing items in store
            Guid tenant = Guid.NewGuid();
            Guid billerId = Guid.NewGuid();
            Guid billeeId = Guid.NewGuid();
            long invoiceId = 20250102020202;

            var existing = new[] { MakeItem(invoiceId, 1, "Old", "USD", 1000, 1, 1000) };
            var newItems = new[] { MakeItem(invoiceId, 1, "New", "USD", 2000, 1, 2000) };

            Arranged arr = ArrangeUpsertFlow(tenant, billerId, billeeId, invoiceId, newItems, existingItems: existing);
            var request = MakeRequest(tenant, billerId, billeeId, invoiceId, newItems);
            var billee = MakeBillee(billerId);

            // When
            await arr.Flow.RunAsync(request, billee, CancellationToken.None);

            // Then previous items are deleted and new ones created
            arr.ItemRepo.Verify(r => r.DeleteAsync(It.Is<IEnumerable<InvoiceItem>>(its => its.Any(i => i.ID == 1 && i.Subject == "Old")), true, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
            arr.ItemRepo.Verify(r => r.CreateAsync(It.Is<IEnumerable<InvoiceItem>>(its => its.Any(i => i.ID == 1 && i.Subject == "New")), true, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task Billee_is_fetched_when_not_supplied()
        {
            // Given billee is not supplied to flow
            Guid tenant = Guid.NewGuid();
            Guid billerId = Guid.NewGuid();
            Guid billeeId = Guid.NewGuid();
            long invoiceId = 20250103030303;
            var items = new[] { MakeItem(invoiceId, 1, "Service", "USD", 1000, 1, 1000) };

            Arranged arr = ArrangeUpsertFlow(tenant, billerId, billeeId, invoiceId, items);
            var request = MakeRequest(tenant, billerId, billeeId, invoiceId, items);

            // When
            await arr.Flow.RunAsync(request, billee: null, CancellationToken.None);

            // Then billee is retrieved by repo
            arr.BilleeRepo.Verify(r => r.RetrieveAsync(Billee.BuildPartitionKey(billerId), Billee.BuildRowKey(billeeId), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task Unknown_biller_results_in_not_found_and_no_persistence()
        {
            // Given biller not found
            Guid tenant = Guid.NewGuid();
            Guid billerId = Guid.NewGuid();
            Guid billeeId = Guid.NewGuid();
            long invoiceId = 20250104040404;
            var items = new[] { MakeItem(invoiceId, 1, "Service", "USD", 1000, 1, 1000) };
            var request = MakeRequest(tenant, billerId, billeeId, invoiceId, items);

            // Arrange with profile returning null
            IOptions<BillingOptions> options = Microsoft.Extensions.Options.Options.Create(DefaultBillingOptions);
            var invoiceRepo = new Mock<IRepository<Invoice>>(MockBehavior.Strict);
            var invoiceDomainRepo = new Mock<IDomainRepository<InvoiceDomain, Invoice>>(MockBehavior.Strict);
            var itemRepo = new Mock<IRepository<InvoiceItem>>(MockBehavior.Strict);
            var billeeRepo = new Mock<IRepository<Billee>>(MockBehavior.Strict);
            var profileService = new Mock<IProfileService<Biller>>(MockBehavior.Strict);
            profileService
                .Setup(s => s.RetrieveAsync(tenant, billerId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Biller?)null);

            var flow = new UpsertFlow(invoiceDomainRepo.Object, itemRepo.Object, billeeRepo.Object, profileService.Object);

            // When
            Func<Task> act = async () => await flow.RunAsync(request, MakeBillee(billerId), CancellationToken.None);

            // Then
            await act.Should().ThrowAsync<ApplicationException>();
            itemRepo.Verify(r => r.CreateAsync(It.IsAny<IEnumerable<InvoiceItem>>(), true, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
            itemRepo.Verify(r => r.DeleteAsync(It.IsAny<IEnumerable<InvoiceItem>>(), true, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            invoiceRepo.VerifyNoOtherCalls();
        }

        [TestMethod]
        public async Task Unknown_billee_results_in_not_found_and_no_persistence()
        {
            // Given billee not found when not supplied
            Guid tenant = Guid.NewGuid();
            Guid billerId = Guid.NewGuid();
            Guid billeeId = Guid.NewGuid();
            long invoiceId = 20250105050505;
            var items = new[] { MakeItem(invoiceId, 1, "Service", "USD", 1000, 1, 1000) };

            IOptions<BillingOptions> options = Microsoft.Extensions.Options.Options.Create(DefaultBillingOptions);
            var invoiceRepo = new Mock<IRepository<Invoice>>(MockBehavior.Strict);
            var invoiceDomainRepo = new Mock<IDomainRepository<InvoiceDomain, Invoice>>(MockBehavior.Strict);
            var itemRepo = new Mock<IRepository<InvoiceItem>>(MockBehavior.Strict);
            var billeeRepo = new Mock<IRepository<Billee>>(MockBehavior.Strict);
            var profileService = new Mock<IProfileService<Biller>>(MockBehavior.Strict);

            // Known biller
            profileService
                .Setup(s => s.RetrieveAsync(tenant, billerId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Mock<Biller>(MockBehavior.Loose).Object);

            // Billee not found
            billeeRepo
                .Setup(r => r.RetrieveAsync(Billee.BuildPartitionKey(billerId), Billee.BuildRowKey(billeeId), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Billee?)null);

            var flow = new UpsertFlow(invoiceDomainRepo.Object, itemRepo.Object, billeeRepo.Object, profileService.Object);
            var request = MakeRequest(tenant, billerId, billeeId, invoiceId, items);

            // When
            Func<Task> act = async () => await flow.RunAsync(request, billee: null, CancellationToken.None);

            // Then
            await act.Should().ThrowAsync<ApplicationException>();
            itemRepo.Verify(r => r.CreateAsync(It.IsAny<IEnumerable<InvoiceItem>>(), true, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
            itemRepo.Verify(r => r.DeleteAsync(It.IsAny<IEnumerable<InvoiceItem>>(), true, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            invoiceRepo.VerifyNoOtherCalls();
        }

        [TestMethod]
        public async Task CancellationToken_is_propagated()
        {
            // Given a cancellable token and normal data
            Guid tenant = Guid.NewGuid();
            Guid billerId = Guid.NewGuid();
            Guid billeeId = Guid.NewGuid();
            long invoiceId = 20250106060606;
            var items = new[] { MakeItem(invoiceId, 1, "Service", "USD", 1000, 1, 1000) };
            var request = MakeRequest(tenant, billerId, billeeId, invoiceId, items);
            using var cts = new CancellationTokenSource();

            IOptions<BillingOptions> options = Microsoft.Extensions.Options.Options.Create(DefaultBillingOptions);
            var invoiceRepo = new Mock<IRepository<Invoice>>(MockBehavior.Strict);
            var invoiceDomainRepo = new Mock<IDomainRepository<InvoiceDomain, Invoice>>(MockBehavior.Strict);
            var itemRepo = new Mock<IRepository<InvoiceItem>>(MockBehavior.Strict);
            var billeeRepo = new Mock<IRepository<Billee>>(MockBehavior.Strict);
            var profileService = new Mock<IProfileService<Biller>>(MockBehavior.Strict);

            var domain = new InvoiceDomain(options, new Lazy<IRepository<Invoice>>(() => invoiceRepo.Object), Enumerable.Empty<IDomainEventHandler<IDomain<Invoice>>>());

            profileService
                .Setup(s => s.RetrieveAsync(tenant, billerId, It.IsAny<bool>(), It.Is<CancellationToken>(t => t == cts.Token)))
                .ReturnsAsync(new Mock<Biller>(MockBehavior.Loose).Object);

            billeeRepo
                .Setup(r => r.RetrieveAsync(Billee.BuildPartitionKey(billerId), Billee.BuildRowKey(billeeId), It.IsAny<IList<string>?>(), It.Is<CancellationToken>(t => t == cts.Token)))
                .ReturnsAsync(MakeBillee(billerId));

            invoiceDomainRepo
                .Setup(r => r.BuildAsync(It.IsAny<Invoice>(), It.Is<CancellationToken>(t => t == cts.Token)))
                .Returns((Invoice inv, CancellationToken _) =>
                {
                    string pk = Invoice.BuildPartitionKey(inv.Biller);
                    string rk = Invoice.BuildRowKey(inv.GetID());
                    domain.Initialize(pk, rk);

                    invoiceRepo
                        .Setup(x => x.ExistsAsync(pk, rk, It.Is<CancellationToken>(t => t == cts.Token)))
                        .ReturnsAsync(true);
                    invoiceRepo
                        .Setup(x => x.RetrieveAsync(pk, rk, It.IsAny<IList<string>?>(), It.Is<CancellationToken>(t => t == cts.Token)))
                        .ReturnsAsync(inv);
                    invoiceRepo
                        .Setup(x => x.UpdateAsync(It.IsAny<IEnumerable<Invoice>>(), It.IsAny<bool>(), false, It.Is<CancellationToken>(t => t == cts.Token)))
                        .ReturnsAsync((IEnumerable<Invoice> e, bool _, bool __, CancellationToken ___) => e);
                    invoiceRepo
                        .Setup(x => x.CreateAsync(It.IsAny<IEnumerable<Invoice>>(), It.IsAny<bool>(), null, It.IsAny<CancellationToken>()))
                        .ReturnsAsync((IEnumerable<Invoice> e, bool _, DateTimeOffset? __, CancellationToken ___) => e);
                    return Task.FromResult(domain);
                });

            itemRepo
                .Setup(r => r.GetAsync(InvoiceItem.BuildPartitionKey(invoiceId), It.IsAny<IList<string>?>(), It.Is<CancellationToken>(t => t == cts.Token)))
                .Returns(YieldAsync(Array.Empty<InvoiceItem>()));
            itemRepo
                .Setup(r => r.CreateAsync(It.IsAny<IEnumerable<InvoiceItem>>(), It.IsAny<bool>(), It.IsAny<DateTimeOffset?>(), It.Is<CancellationToken>(t => t == cts.Token)))
                .ReturnsAsync((IEnumerable<InvoiceItem> e, bool _, DateTimeOffset? __, CancellationToken ___) => e);

            var flow = new UpsertFlow(invoiceDomainRepo.Object, itemRepo.Object, billeeRepo.Object, profileService.Object);

            // When
            await flow.RunAsync(request, billee: null, cts.Token);

            // Then: verifications above ensure token reached all collaborators
            Assert.IsTrue(true);
        }

        [TestMethod]
        public async Task Business_fields_from_request_are_applied()
        {
            // Given business fields in request
            Guid tenant = Guid.NewGuid();
            Guid billerId = Guid.NewGuid();
            Guid billeeId = Guid.NewGuid();
            long invoiceId = 20250107070707;
            DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(-30);
            DateTimeOffset end = DateTimeOffset.UtcNow;
            DateTimeOffset due = DateTimeOffset.UtcNow.AddDays(10);

            var items = new[] { MakeItem(invoiceId, 1, "Service", "USD", 2000, 1, 2000) };
            var request = MakeRequest(tenant, billerId, billeeId, invoiceId, items,
                terms: "  Pay within 10 days  ", paymentInstructions: "  Online only  ",
                invoiceCycle: (int)InvoiceCycle.Range, start: start, end: end, dueBy: due, settledCents: 1234);

            Arranged arr = ArrangeUpsertFlow(tenant, billerId, billeeId, invoiceId, items);

            // When
            await arr.Flow.RunAsync(request, billee: null, CancellationToken.None);

            // Then fields are trimmed and set
            arr.InvoiceRepo.Verify(r => r.CreateAsync(It.Is<IEnumerable<Invoice>>(e => e.Any(inv =>
                inv.InvoiceCycle == (int)InvoiceCycle.Range &&
                inv.BillingPeriodStartDay == start && inv.BillingPeriodEndDay == end &&
                inv.DueBy == due &&
                inv.SettledCents == 1234 &&
                inv.Terms == "Pay within 10 days" && inv.PaymentInstructions == "Online only"
            )), true, null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [TestMethod]
        public async Task Reissue_with_different_items_updates_totals()
        {
            // Given first issue then reissue with different items
            Guid tenant = Guid.NewGuid();
            Guid billerId = Guid.NewGuid();
            Guid billeeId = Guid.NewGuid();
            long invoiceId = 20250108080808;

            var itemsA = new[] { MakeItem(invoiceId, 1, "A", "USD", 1000, 2, 2000) };
            var itemsB = new[] { MakeItem(invoiceId, 1, "B", "USD", 2500, 1, 2500) };

            // First run: no existing items
            Arranged arr = ArrangeUpsertFlow(tenant, billerId, billeeId, invoiceId, itemsA);
            await arr.Flow.RunAsync(MakeRequest(tenant, billerId, billeeId, invoiceId, itemsA), billee: null, CancellationToken.None);

            // Second run: existing items are now itemsA
            Arranged arr2 = ArrangeUpsertFlow(tenant, billerId, billeeId, invoiceId, itemsB, existingItems: itemsA);
            await arr2.Flow.RunAsync(MakeRequest(tenant, billerId, billeeId, invoiceId, itemsB), billee: null, CancellationToken.None);

            // Then new total should reflect itemsB (2500)
            arr2.InvoiceRepo.Verify(r => r.CreateAsync(It.Is<IEnumerable<Invoice>>(e => e.Any(inv => inv.GrandTotalCents == 2500)), true, null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            arr2.ItemRepo.Verify(r => r.DeleteAsync(It.IsAny<IEnumerable<InvoiceItem>>(), true, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
            arr2.ItemRepo.Verify(r => r.CreateAsync(It.Is<IEnumerable<InvoiceItem>>(its => its.Any(i => i.Subject == "B")), true, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
