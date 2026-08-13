using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Niobium.Invoicing;
using Niobium.Invoicing.Flows;

namespace Niobium.Invoicing.Core.Tests.Flows
{
    [TestClass]
    public class IssueFlowTests
    {
        private static IssueInvoiceRequest MakeRequest(bool notify = false)
        {
            return new IssueInvoiceRequest
            {
                Tenant = Guid.NewGuid(),
                BillerID = Guid.NewGuid(),
                BilleeID = Guid.NewGuid(),
                InvoiceID = 20250101010101,
                InvoiceItems = new List<InvoiceItem>
                {
                    new InvoiceItem
                    {
                        Invoice = Invoice.ParseID(20250101010101),
                        ID = 1,
                        Subject = "Service",
                        UnitPriceCurrency = "USD",
                        UnitPriceCents = 1000,
                        Quantity = 1,
                        LineTotalCurrency = "USD",
                        LineTotalCents = 1000,
                    }
                },
                NotifyBillee = notify,
            };
        }

        private static Billee MakeBillee(Guid biller)
        {
            return new Billee
            {
                Biller = biller,
                ID = Guid.NewGuid(),
                Name = "John Customer",
                Culture = "en-NZ",
                Currency = "USD",
                TimeZone = "Pacific/Auckland",
                Email = "john@example.com"
            };
        }

        [TestMethod]
        public async Task Issue_without_notification_only_updates_invoice()
        {
            // Given a request that should not notify the billee
            var req = MakeRequest(notify: false);
            var billee = MakeBillee(req.BillerID);

            var upsert = new Mock<UpsertFlow>(MockBehavior.Strict, null!, null!, null!, null!);
            upsert.Setup(x => x.RunAsync(req, billee, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var email = new Mock<EmailFlow>(MockBehavior.Strict, null!, null!, null!, null!, null!);

            var flow = new IssueFlow(upsert.Object, email.Object);

            // When
            await flow.RunAsync(req, billee, CancellationToken.None);

            // Then only upsert is invoked
            upsert.Verify(x => x.RunAsync(req, billee, It.IsAny<CancellationToken>()), Times.Once);
            email.Verify(x => x.RunAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestMethod]
        public async Task Issue_with_notification_updates_and_sends_email()
        {
            // Given a request that should notify the billee
            var req = MakeRequest(notify: true);
            var billee = MakeBillee(req.BillerID);

            var upsert = new Mock<UpsertFlow>(MockBehavior.Strict, null!, null!, null!, null!);
            upsert.Setup(x => x.RunAsync(req, billee, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var email = new Mock<EmailFlow>(MockBehavior.Strict, null!, null!, null!, null!, null!);
            email.Setup(x => x.RunAsync(req.BillerID, req.InvoiceID, It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

            var flow = new IssueFlow(upsert.Object, email.Object);

            // When
            await flow.RunAsync(req, billee, CancellationToken.None);

            // Then both update and notify occur
            upsert.Verify(x => x.RunAsync(req, billee, It.IsAny<CancellationToken>()), Times.Once);
            email.Verify(x => x.RunAsync(req.BillerID, req.InvoiceID, It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task When_upsert_fails_no_notification_is_sent()
        {
            // Given upsert throws (e.g., billee or biller not found)
            var req = MakeRequest(notify: true);
            var billee = MakeBillee(req.BillerID);

            var upsert = new Mock<UpsertFlow>(MockBehavior.Strict, null!, null!, null!, null!);
            upsert.Setup(x => x.RunAsync(req, billee, It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new ApplicationException(InternalError.NotFound, "Billee does not exist."));

            var email = new Mock<EmailFlow>(MockBehavior.Strict, null!, null!, null!, null!, null!);

            var flow = new IssueFlow(upsert.Object, email.Object);

            // When
            var act = () => flow.RunAsync(req, billee, CancellationToken.None);

            // Then
            await act.Should().ThrowAsync<ApplicationException>();
            email.Verify(x => x.RunAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestMethod]
        public async Task CancellationToken_is_propagated_to_children()
        {
            // Given a cancellable token
            var req = MakeRequest(notify: true);
            var billee = MakeBillee(req.BillerID);
            using var cts = new CancellationTokenSource();

            CancellationToken? tokenSeenByUpsert = null;
            CancellationToken? tokenSeenByEmail = null;

            var upsert = new Mock<UpsertFlow>(MockBehavior.Strict, null!, null!, null!, null!);
            upsert.Setup(x => x.RunAsync(req, billee, It.IsAny<CancellationToken>()))
                  .Callback<IssueInvoiceRequest, Billee?, CancellationToken>((_, __, t) => tokenSeenByUpsert = t)
                  .Returns(Task.CompletedTask);

            var email = new Mock<EmailFlow>(MockBehavior.Strict, null!, null!, null!, null!, null!);
            email.Setup(x => x.RunAsync(req.BillerID, req.InvoiceID, It.IsAny<CancellationToken>()))
                 .Callback<Guid, long, CancellationToken>((_, __, t) => tokenSeenByEmail = t)
                 .Returns(Task.CompletedTask);

            var flow = new IssueFlow(upsert.Object, email.Object);

            // When
            await flow.RunAsync(req, billee, cts.Token);

            // Then the same token is observed by both children
            tokenSeenByUpsert.Should().Be(cts.Token);
            tokenSeenByEmail.Should().Be(cts.Token);
        }
    }
}
