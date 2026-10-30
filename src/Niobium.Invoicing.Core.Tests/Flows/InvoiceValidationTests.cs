using FluentAssertions;
using Niobium.Finance;
using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Options;

namespace Niobium.Invoicing.Core.Tests.Flows;

[TestClass]
public class InvoiceValidationTests
{
    [TestMethod]
    [DataRow("tenant")]
    [DataRow("biller")]
    [DataRow("billee")]
    [DataRow("invoice-id")]
    [DataRow("invoice-date")]
    [DataRow("cycle")]
    [DataRow("period")]
    [DataRow("null-items")]
    [DataRow("null-line")]
    [DataRow("null-subject")]
    [DataRow("duplicate")]
    [DataRow("parent")]
    [DataRow("profile-owner")]
    [DataRow("profile-group")]
    [DataRow("customer-owner")]
    [DataRow("customer-id")]
    [DataRow("biller-currency")]
    [DataRow("customer-currency")]
    [DataRow("unit-currency")]
    [DataRow("line-currency")]
    [DataRow("country")]
    [DataRow("due")]
    [DataRow("settlement")]
    [DataRow("line-overflow")]
    [DataRow("subtotal-overflow")]
    [DataRow("tax-overflow")]
    [DataRow("total-overflow")]
    [DataRow("balance-overflow")]
    public async Task Invalid_input_fails_before_any_write_or_notification(string invalid)
    {
        IssuanceScenario scenario = new();
        scenario.Request.NotifyBillee = true;
        InvoiceItem line = scenario.Request.InvoiceItems[0];
        switch (invalid)
        {
            case "tenant":
                scenario.Request.Tenant = Guid.Empty;
                break;
            case "biller":
                scenario.Request.BillerID = Guid.Empty;
                break;
            case "billee":
                scenario.Request.BilleeID = Guid.Empty;
                break;
            case "invoice-id":
                scenario.Request.InvoiceID = 0;
                break;
            case "invoice-date":
                scenario.Request.InvoiceID = long.MaxValue;
                break;
            case "cycle":
                scenario.Request.InvoiceCycle = 99;
                break;
            case "period":
                scenario.Request.BillingPeriodStartDay = DateTimeOffset.UtcNow;
                scenario.Request.BillingPeriodEndDay = DateTimeOffset.UtcNow.AddDays(-1);
                break;
            case "null-items":
                scenario.Request.InvoiceItems = null!;
                break;
            case "null-line":
                scenario.Request.InvoiceItems.Add(null!);
                break;
            case "null-subject":
                line.Subject = null!;
                break;
            case "duplicate":
                scenario.Request.InvoiceItems.Add(IssuanceScenario.Copy(line));
                break;
            case "parent":
                line.Invoice = line.Invoice.AddMilliseconds(-1);
                break;
            case "profile-owner":
                scenario.Biller.RowKey = Guid.NewGuid();
                break;
            case "profile-group":
                scenario.Biller.PartitionKey = Guid.NewGuid();
                break;
            case "customer-owner":
                scenario.Billee.Biller = Guid.NewGuid();
                break;
            case "customer-id":
                scenario.Billee.ID = Guid.NewGuid();
                break;
            case "biller-currency":
                scenario.Biller.Currency = "USD";
                break;
            case "customer-currency":
                scenario.Billee.Currency = "AUD";
                break;
            case "unit-currency":
                line.UnitPriceCurrency = "AUD";
                break;
            case "line-currency":
                line.LineTotalCurrency = "AUD";
                break;
            case "country":
                scenario.Billee.Country = "US";
                break;
            case "due":
                scenario.Request.DueBy = line.Invoice.AddTicks(-1);
                break;
            case "settlement":
                scenario.Request.Settled = new Amount(1L, "AUD");
                break;
            case "line-overflow":
                line.UnitPriceCents = long.MaxValue;
                line.Quantity = 2;
                break;
            case "subtotal-overflow":
                line.UnitPriceCents = long.MaxValue;
                InvoiceItem extra = IssuanceScenario.Copy(line);
                extra.ID = 2;
                extra.UnitPriceCents = 1;
                scenario.Request.InvoiceItems.Add(extra);
                break;
            case "tax-overflow":
                line.UnitPriceCents = long.MaxValue;
                scenario.Biller.TaxRatePercentile = 20000;
                break;
            case "total-overflow":
                line.UnitPriceCents = long.MaxValue;
                break;
            case "balance-overflow":
                scenario.Request.Settled = new Amount(long.MinValue, "NZD");
                break;
        }

        Func<Task> action = () => scenario.RunAsync();
        (await action.Should().ThrowAsync<ApplicationException>()).Which.ErrorCode.Should().Be(400);
        scenario.InvoiceWrites.Should().Be(0);
        scenario.ItemWrites.Should().Be(0);
        scenario.Messages.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Embedded_queue_customer_cannot_bypass_ownership_validation()
    {
        IssuanceScenario scenario = new();
        Billee embedded = IssuanceScenario.Copy(scenario.Billee);
        embedded.Biller = Guid.NewGuid();
        Func<Task> action = () => scenario.RunAsync(embedded);
        (await action.Should().ThrowAsync<ApplicationException>()).Which.ErrorCode.Should().Be(400);
        scenario.InvoiceWrites.Should().Be(0);
    }

    [TestMethod]
    public void Command_sanitation_preserves_conflicting_customer_id_for_validation()
    {
        IssuanceScenario scenario = new();
        IssueInvoiceCommand command = new()
        {
            Tenant = scenario.Request.Tenant,
            BillerID = scenario.Request.BillerID,
            BilleeID = Guid.NewGuid(),
            Billee = scenario.Billee,
            InvoiceID = scenario.Request.InvoiceID,
            InvoiceItems = scenario.Request.InvoiceItems
        };
        Guid supplied = command.BilleeID;
        command.Sanitize();
        command.BilleeID.Should().Be(supplied);
        command.BilleeID = Guid.Empty;
        command.Sanitize();
        command.BilleeID.Should().Be(scenario.Billee.ID);
        command.Billee = null!;
        command.Invoking(c => c.Sanitize()).Should().NotThrow();
        Action validate = () => InvoiceValidation.ValidateRequest(command);
        validate.Should().Throw<ApplicationException>().Which.ErrorCode.Should().Be(400);
    }

    [TestMethod]
    [DataRow("NZD")]
    [DataRow("AUD")]
    public async Task Supported_currency_preserves_default_due_date_and_accepts_zero_settlement(string currency)
    {
        IssuanceScenario scenario = new();
        scenario.Biller.Currency = scenario.Billee.Currency = currency;
        scenario.Request.InvoiceItems[0].UnitPriceCurrency = scenario.Request.InvoiceItems[0].LineTotalCurrency = currency;
        DateTimeOffset before = DateTimeOffset.UtcNow.AddDays(7);
        await scenario.RunAsync();
        scenario.Stored!.DueBy!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow.AddDays(7));
        scenario.Stored.SettledCents.Should().Be(0);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task Quantity_policy_and_midpoint_rounding_are_not_redefined(int quantity)
    {
        IssuanceScenario scenario = new();
        scenario.Request.InvoiceItems[0].Quantity = quantity;
        scenario.Request.InvoiceItems[0].UnitPriceCents = 10;
        await scenario.RunAsync();
        scenario.Stored!.SubtotalCents.Should().Be(10 * quantity);
        scenario.Stored.TaxCents.Should().Be(quantity == 0 ? 0 : -2);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  requested value  ")]
    public async Task Display_overrides_flow_through_persistence_to_rendering(string? value)
    {
        IssuanceScenario scenario = new();
        scenario.Request.Particulars = scenario.Request.Reference = scenario.Request.PaymentInstructions = value;
        await scenario.RunAsync();
        Invoice defaults = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        scenario.Stored!.Particulars.Should().Be(value?.Trim() ?? defaults.Particulars);
        scenario.Stored.Reference.Should().Be(value?.Trim() ?? defaults.Reference);
        scenario.Stored.PaymentInstructions.Should().Be(value?.Trim() ?? defaults.PaymentInstructions);
        scenario.Stored.Token = "synthetic-link-token";
        InvoiceDomain domain = new(Microsoft.Extensions.Options.Options.Create(new BillingOptions
        {
            GetInvoiceEndpoint = "https://invoice.example.invalid",
            InvoiceTokenSecretSalt = "synthetic-secret"
        }), new Lazy<IRepository<Invoice>>(() => scenario.InvoiceRepo.Object), []);
        domain.Initialize(scenario.Stored);
        string html = await domain.BuildHTMLAsync("synthetic-link-token", CancellationToken.None);
        if (!string.IsNullOrEmpty(value))
        {
            html.Should().Contain(value.Trim());
        }
        if (value == null)
        {
            html.Should().Contain("Bank transfer");
        }
        if (value == "")
        {
            html.Should().NotContain("Bank transfer");
        }
    }
}
