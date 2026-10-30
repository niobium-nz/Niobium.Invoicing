using Azure;
using FluentAssertions;
using Moq;
using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.Domains;
using System.Globalization;

namespace Niobium.Invoicing.Core.Tests.Flows;

[TestClass]
public class InvoiceReplayTests
{
    [TestMethod]
    public async Task Storage_checkpoints_are_not_exposed_in_invoice_JSON()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        scenario.Stored!.IssuanceState.Should().Be(InvoiceContent.Ready);
        string json = JsonMarshaller.Marshall(scenario.Stored);
        json.ToLowerInvariant().Should().NotContain("issuance");
        JsonMarshaller.Unmarshall<Invoice>(json)!.IssuanceState.Should().BeNull();
    }

    [TestMethod]
    public async Task Exact_replay_before_notification_preserves_snapshot_and_does_not_write()
    {
        IssuanceScenario scenario = new()
        {
            DropWriteEtags = true
        };
        await scenario.RunAsync();
        Invoice before = InvoiceContent.Copy(scenario.Stored!);
        int writes = scenario.InvoiceWrites;
        int itemWrites = scenario.ItemWrites;
        scenario.Biller.BusinessName = "New profile name";
        scenario.Billee.Email = "changed@example.invalid";
        await scenario.RunAsync();
        scenario.InvoiceWrites.Should().Be(writes);
        scenario.ItemWrites.Should().Be(itemWrites);
        scenario.Stored!.BillerName.Should().Be(before.BillerName);
        scenario.Stored.RecipientEmail.Should().Be(before.RecipientEmail);
        scenario.Stored.DueBy.Should().Be(before.DueBy);
        scenario.Stored.IssuanceState.Should().Be(InvoiceContent.Ready);
    }

    [TestMethod]
    public async Task Changed_content_before_freeze_is_conditionally_prepared()
    {
        IssuanceScenario scenario = new()
        {
            DropWriteEtags = true
        };
        await scenario.RunAsync();
        string oldVersion = scenario.Stored!.ETag!;
        scenario.Request.InvoiceItems[0].Description = "Revised description with the same total";
        await scenario.RunAsync();
        scenario.Stored!.ETag.Should().NotBe(oldVersion);
        scenario.Stored.IssuanceState.Should().Be(InvoiceContent.Ready);
        scenario.Items.Single().Description.Should().Be("Revised description with the same total");
    }

    [TestMethod]
    public async Task Exact_replay_after_freeze_preserves_message_token_snapshot_and_enqueue_timestamp()
    {
        IssuanceScenario scenario = new()
        {
            DropWriteEtags = true
        };
        scenario.Request.NotifyBillee = true;
        await scenario.RunAsync();
        Invoice before = InvoiceContent.Copy(scenario.Stored!);
        string message = JsonMarshaller.Marshall(scenario.Messages.Single().Value!);
        int writes = scenario.InvoiceWrites;
        scenario.Options.InvoiceTokenSecretSalt = "changed-secret";
        scenario.Options.GetInvoiceEndpoint = "https://changed.example.invalid";
        scenario.Biller.BusinessName = "Changed profile";
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            await scenario.RunAsync();
        }
        finally { CultureInfo.CurrentCulture = previous; }
        scenario.Messages.Should().HaveCount(2);
        JsonMarshaller.Marshall(scenario.Messages[1].Value!).Should().Be(message);
        scenario.Messages[1].ID.Should().Be(before.GetFullID());
        scenario.Stored!.Token.Should().Be(before.Token);
        scenario.Stored.Delivered.Should().Be(before.Delivered);
        scenario.InvoiceWrites.Should().Be(writes);
    }

    [TestMethod]
    [DataRow("item")]
    [DataRow("terms")]
    [DataRow("due")]
    [DataRow("customer")]
    public async Task Changed_request_after_freeze_conflicts_before_writes_or_enqueue(string change)
    {
        IssuanceScenario scenario = new();
        scenario.Request.NotifyBillee = true;
        await scenario.RunAsync();
        int writes = scenario.InvoiceWrites;
        Billee? customer = null;
        switch (change)
        {
            case "item":
                scenario.Request.InvoiceItems[0].Description = "Changed";
                break;
            case "terms":
                scenario.Request.Terms = "Changed";
                break;
            case "due":
                scenario.Request.DueBy = scenario.Stored!.DueBy!.Value.AddDays(1);
                break;
            case "customer":
                customer = IssuanceScenario.Copy(scenario.Billee);
                customer.Email = "changed@example.invalid";
                break;
        }
        Func<Task> action = () => scenario.RunAsync(customer);
        (await action.Should().ThrowAsync<ApplicationException>()).Which.ErrorCode.Should().Be(409);
        scenario.InvoiceWrites.Should().Be(writes);
        scenario.Messages.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task Stale_invoice_update_prevents_item_changes()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        int writes = scenario.ItemWrites;
        scenario.Request.InvoiceItems[0].UnitPriceCents = 20000;
        scenario.BeforeInvoiceUpdate = () => scenario.Stored!.ETag = "concurrent-writer";
        Func<Task> action = () => scenario.RunAsync();
        (await action.Should().ThrowAsync<ApplicationException>()).Which.ErrorCode.Should().Be(409);
        scenario.ItemWrites.Should().Be(writes);
        scenario.Stored!.GrandTotalCents.Should().Be(11500);
    }

    [TestMethod]
    public async Task Concurrent_create_conflict_does_not_replace_existing_items()
    {
        IssuanceScenario scenario = new()
        {
            FailInvoiceWrite = true
        };
        Func<Task> action = () => scenario.RunAsync();
        (await action.Should().ThrowAsync<ApplicationException>()).Which.ErrorCode.Should().Be(409);
        scenario.ItemWrites.Should().Be(0);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Definite_item_failure_blocks_notification_and_exact_retry_recovers(bool delete)
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        scenario.Request.NotifyBillee = true;
        scenario.Request.InvoiceItems[0].UnitPriceCents = 20000;
        InvoiceItem extra = IssuanceScenario.Copy(scenario.Request.InvoiceItems[0]);
        extra.ID = 2;
        scenario.Request.InvoiceItems.Add(extra);
        scenario.FailDelete = delete;
        scenario.FailCreate = !delete;
        Func<Task> action = () => scenario.RunAsync();
        await action.Should().ThrowAsync<RequestFailedException>();
        scenario.Stored!.IssuanceState.Should().Be(InvoiceContent.Retryable);
        scenario.Stored.Token.Should().BeNull();
        scenario.Messages.Should().BeEmpty();
        scenario.FailDelete = scenario.FailCreate = false;
        await scenario.RunAsync();
        scenario.Items.Should().HaveCount(2);
        scenario.Stored!.GrandTotalCents.Should().Be(46000);
        scenario.Stored.IssuanceState.Should().Be(InvoiceContent.Frozen);
        scenario.Messages.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task Changed_retry_cannot_replace_an_incomplete_preparation()
    {
        IssuanceScenario scenario = new()
        {
            FailCreate = true
        };
        Func<Task> first = () => scenario.RunAsync();
        await first.Should().ThrowAsync<RequestFailedException>();
        scenario.FailCreate = false;
        scenario.Request.InvoiceItems[0].Subject = "Different content";
        Func<Task> retry = () => scenario.RunAsync();
        (await retry.Should().ThrowAsync<ApplicationException>()).Which.ErrorCode.Should().Be(409);
    }

    [TestMethod]
    public async Task Interrupted_preparation_is_not_taken_over_automatically()
    {
        IssuanceScenario scenario = new();
        scenario.ItemRepo.Setup(r => r.CreateAsync(It.IsAny<IEnumerable<InvoiceItem>>(), false, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Synthetic unknown storage outcome"));
        Func<Task> first = () => scenario.RunAsync();
        await first.Should().ThrowAsync<TimeoutException>();
        scenario.Stored!.IssuanceState.Should().Be(InvoiceContent.Preparing);
        Func<Task> retry = () => scenario.RunAsync();
        (await retry.Should().ThrowAsync<ApplicationException>()).Which.ErrorCode.Should().Be(409);
        scenario.Messages.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Uncertain_enqueue_stays_frozen_and_exact_retry_republishes_identical_message()
    {
        IssuanceScenario scenario = new()
        {
            FailEnqueue = true
        };
        scenario.Request.NotifyBillee = true;
        Func<Task> first = () => scenario.RunAsync();
        await first.Should().ThrowAsync<TimeoutException>();
        scenario.Stored!.IssuanceState.Should().Be(InvoiceContent.Frozen);
        scenario.Stored.Delivered.Should().BeNull();
        string queued = JsonMarshaller.Marshall(scenario.Messages.Single().Value!);
        scenario.FailEnqueue = false;
        await scenario.RunAsync();
        JsonMarshaller.Marshall(scenario.Messages[1].Value!).Should().Be(queued);
        scenario.Stored!.Delivered.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Legacy_saved_token_freezes_content_and_remains_usable()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        scenario.Stored!.IssuanceState = null;
        scenario.Stored.IssuanceFingerprint = null;
        scenario.Stored.Token = "legacy-synthetic-token";
        scenario.Request.NotifyBillee = true;
        await scenario.RunAsync();
        scenario.Stored.Token.Should().Be("legacy-synthetic-token");
        scenario.Stored.IssuanceState.Should().Be(InvoiceContent.Frozen);
        scenario.Request.InvoiceItems[0].Quantity = 2;
        Func<Task> changed = () => scenario.RunAsync();
        (await changed.Should().ThrowAsync<ApplicationException>()).Which.ErrorCode.Should().Be(409);
        scenario.Stored.Token.Should().Be("legacy-synthetic-token");
    }

    [TestMethod]
    public async Task Null_display_values_inherit_original_snapshot_defaults_after_an_edit()
    {
        IssuanceScenario scenario = new();
        scenario.Request.PaymentInstructions = "Override";
        await scenario.RunAsync();
        scenario.Biller.PaymentInstructions = "New live profile default";
        scenario.Request.PaymentInstructions = null;
        await scenario.RunAsync();
        scenario.Stored!.PaymentInstructions.Should().Be("Bank transfer");
    }
}
