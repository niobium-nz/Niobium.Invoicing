using FluentAssertions;
using Moq;
using Niobium.Finance;
using Niobium.Invoicing.Core.Tests.Support;

namespace Niobium.Invoicing.Core.Tests.Flows;

[TestClass]
public class UpsertFlowTests
{
    [TestMethod]
    public async Task New_invoice_is_issued_and_items_are_persisted()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        scenario.Stored!.GrandTotalCents.Should().Be(11500);
        scenario.Items.Single().LineTotalCents.Should().Be(10000);
        scenario.Stored.Biller.Should().Be(scenario.Request.BillerID);
        scenario.Stored.DueBy.Should().NotBeNull();
        scenario.Stored.PaymentInstructions.Should().Be("Bank transfer");
    }

    [TestMethod]
    public async Task Existing_items_are_replaced()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        scenario.Request.InvoiceItems[0].ID = 2;
        await scenario.RunAsync();
        scenario.Items.Select(i => i.ID).Should().Equal(2);
    }

    [TestMethod]
    public async Task Supplied_billee_does_not_require_customer_lookup()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync(scenario.Billee);
        scenario.BilleeRepo.Verify(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Missing_party_fails_without_persistence(bool biller)
    {
        IssuanceScenario scenario = new()
        {
            MissingBiller = biller,
            MissingBillee = !biller
        };
        Func<Task> action = () => scenario.RunAsync();
        (await action.Should().ThrowAsync<ApplicationException>()).Which.ErrorCode.Should().Be(404);
        scenario.InvoiceWrites.Should().Be(0);
        scenario.ItemWrites.Should().Be(0);
    }

    [TestMethod]
    public async Task CancellationToken_is_propagated()
    {
        IssuanceScenario scenario = new();
        using CancellationTokenSource cancellation = new();
        await scenario.RunAsync(cancellationToken: cancellation.Token);
        scenario.Tokens.Should().NotBeEmpty().And.OnlyContain(t => t == cancellation.Token);
    }

    [TestMethod]
    public async Task Business_fields_from_request_are_applied()
    {
        IssuanceScenario scenario = new();
        scenario.Request.Terms = "  Net seven days  ";
        scenario.Request.PaymentInstructions = "  Transfer  ";
        scenario.Request.Particulars = "  custom  ";
        scenario.Request.Reference = "  reference  ";
        scenario.Request.DueBy = DateTimeOffset.UtcNow.AddDays(10);
        scenario.Request.Settled = new Amount(20000L, "NZD");
        await scenario.RunAsync();
        scenario.Stored!.Terms.Should().Be("Net seven days");
        scenario.Stored.PaymentInstructions.Should().Be("Transfer");
        scenario.Stored.Particulars.Should().Be("custom");
        scenario.Stored.Reference.Should().Be("reference");
        scenario.Stored.DueBy.Should().Be(scenario.Request.DueBy);
        scenario.Stored.SettledCents.Should().Be(20000);
    }

    [TestMethod]
    public async Task Reissue_with_different_items_updates_totals()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        scenario.Request.InvoiceItems[0].UnitPriceCents = 20000;
        await scenario.RunAsync();
        scenario.Stored!.GrandTotalCents.Should().Be(23000);
        scenario.Items.Single().LineTotalCents.Should().Be(20000);
    }
}
