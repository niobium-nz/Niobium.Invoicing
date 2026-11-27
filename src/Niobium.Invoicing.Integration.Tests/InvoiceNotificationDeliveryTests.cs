using Azure;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Niobium.Invoicing.Delivery;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Server;
using Niobium.Messaging;
using Niobium.Notification;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    public async Task VerifiedReceiptBeforeEnqueueAcknowledgement_PreservesCancellationAndOutOfOrderProgress()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        string biller = Invoice.BuildPartitionKey(request.BillerID);
        string invoice = Invoice.BuildRowKey(request.InvoiceID);
        var domains = host.Services.GetRequiredService<IDomainRepository<InvoiceDomain, Invoice>>();
        InvoiceDomain domain = await domains.GetAsync(biller, invoice, forceLoad: true);
        await domain.FreezeAsync(request.InvoiceItems.ToArray(), "synthetic-integration-salt", default);

        InvoiceLifecycleFlow lifecycle = host.Services.GetRequiredService<InvoiceLifecycleFlow>();
        InvoiceLifecycleStatus cancelled = await lifecycle.CancelAsync(request.BillerID, request.InvoiceID,
            request.Tenant, new() { Operation = Guid.NewGuid(), Reason = "Owned receipt test cancellation" });
        Assert.IsTrue(cancelled.Cancelled);
        Assert.AreEqual(InvoiceDeliveryState.Delivering, cancelled.Delivery);
        Assert.IsNull(cancelled.ConfirmedDeliveredAt);
        Assert.IsNull(cancelled.Notification);
        Invoice before = (await host.Services.GetRequiredService<IRepository<Invoice>>().RetrieveAsync(biller, invoice))!;

        InvoiceNotificationDeliveryConsumer consumer = new(host.Services.GetRequiredService<InvoiceNotificationDeliveryFlow>());
        DateTimeOffset submittedAt = new(2026, 11, 25, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset deliveredAt = submittedAt.AddMinutes(2);
        NotificationDeliveryEvent delivered = Receipt(request, NotificationDeliveryKind.Delivered, "delivered", deliveredAt);
        await Receive(consumer, delivered);
        InvoiceLifecycleStatus confirmed = await lifecycle.GetAsync(request.BillerID, request.InvoiceID, request.Tenant);
        Assert.AreEqual(InvoiceDeliveryState.Delivered, confirmed.Delivery);
        Assert.AreEqual<DateTimeOffset?>(deliveredAt, confirmed.ConfirmedDeliveredAt);
        Assert.IsNull(confirmed.QueuedAt);
        Assert.IsTrue(confirmed.Cancelled);

        await Receive(consumer, Receipt(request, NotificationDeliveryKind.Submitted, "submitted", submittedAt));
        await Receive(consumer, Receipt(request, NotificationDeliveryKind.Delayed, "delay", submittedAt.AddMinutes(1)));
        await Receive(consumer, Receipt(request, NotificationDeliveryKind.Complained, "complaint", submittedAt.AddMinutes(4)));
        await Receive(consumer, Receipt(request, NotificationDeliveryKind.Bounced, "old-bounce", submittedAt.AddMinutes(3)));

        IRepository<InvoiceNotificationDelivery> receipts = host.Services.GetRequiredService<IRepository<InvoiceNotificationDelivery>>();
        InvoiceNotificationDelivery evidence = (await receipts.RetrieveAsync(biller, invoice))!;
        await Receive(consumer, delivered);
        InvoiceNotificationDelivery unchanged = (await receipts.RetrieveAsync(biller, invoice))!;
        Assert.AreEqual(evidence.ETag, unchanged.ETag);

        InvoiceLifecycleStatus final = await lifecycle.GetAsync(request.BillerID, request.InvoiceID, request.Tenant);
        Assert.AreEqual(InvoiceDeliveryState.Delivered, final.Delivery);
        Assert.AreEqual<DateTimeOffset?>(deliveredAt, final.ConfirmedDeliveredAt);
        Assert.AreEqual(new InvoiceNotificationProgress(submittedAt, submittedAt.AddMinutes(1), submittedAt.AddMinutes(4),
            InvoiceNotificationFailure.Complained), final.Notification);
        Assert.IsTrue(final.Cancelled);
        Assert.AreEqual(cancelled.PaymentHistory, final.PaymentHistory);
        Invoice after = (await host.Services.GetRequiredService<IRepository<Invoice>>().RetrieveAsync(biller, invoice))!;
        Assert.AreEqual(before.ETag, after.ETag);
        Assert.AreEqual(before.SettledCents, after.SettledCents);
        Assert.IsEmpty(messages);
    }

    [TestMethod]
    public async Task ReceiptRequiresCanonicalIdentityFrozenInvoiceAndMatchingSubmission()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        InvoiceNotificationDeliveryConsumer consumer = new(host.Services.GetRequiredService<InvoiceNotificationDeliveryFlow>());
        DateTimeOffset at = DateTimeOffset.UtcNow;
        NotificationDeliveryEvent delivered = Receipt(request, NotificationDeliveryKind.Delivered, "delivery", at);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Receive(consumer, delivered, "another-envelope"));
        delivered.Tenant = request.Tenant;
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => Receive(consumer, delivered));
        delivered.Tenant = request.BillerID;

        delivered.Channel = "ContactUs";
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => Receive(consumer, delivered));
        delivered.Channel = "Invoice";

        string operation = delivered.NotificationID;
        delivered.NotificationID = "0" + operation;
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => Receive(consumer, delivered));
        delivered.NotificationID = operation;

        await Assert.ThrowsExactlyAsync<ApplicationException>(() => Receive(consumer, delivered));

        string biller = Invoice.BuildPartitionKey(request.BillerID);
        string invoice = Invoice.BuildRowKey(request.InvoiceID);
        var domains = host.Services.GetRequiredService<IDomainRepository<InvoiceDomain, Invoice>>();
        InvoiceDomain domain = await domains.GetAsync(biller, invoice, forceLoad: true);
        await domain.FreezeAsync(request.InvoiceItems.ToArray(), "synthetic-integration-salt", default);
        NotificationDeliveryEvent submitted = Receipt(request, NotificationDeliveryKind.Submitted, "submitted", at.AddSeconds(-1));
        await Receive(consumer, submitted);
        InvoiceLifecycleStatus progress = await host.Services.GetRequiredService<InvoiceLifecycleFlow>()
            .GetAsync(request.BillerID, request.InvoiceID, request.Tenant);
        Assert.AreEqual(InvoiceDeliveryState.Delivering, progress.Delivery);
        Assert.IsNull(progress.ConfirmedDeliveredAt);
        delivered.SubmissionID = "foreign-submission";
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => Receive(consumer, delivered));
        delivered.SubmissionID = submitted.SubmissionID;

        delivered.ProviderMessageID = "foreign-provider-message";
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => Receive(consumer, delivered));
        delivered.ProviderMessageID = submitted.ProviderMessageID;

        await Receive(consumer, delivered);
        Assert.AreEqual(InvoiceDeliveryState.Delivered, (await host.Services.GetRequiredService<InvoiceLifecycleFlow>()
            .GetAsync(request.BillerID, request.InvoiceID, request.Tenant)).Delivery);
        Assert.IsEmpty(messages);
    }

    [TestMethod]
    public async Task StaleReceiptWriteCannotLoseAnotherFact()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        string biller = Invoice.BuildPartitionKey(request.BillerID);
        string invoice = Invoice.BuildRowKey(request.InvoiceID);
        var invoiceDomains = host.Services.GetRequiredService<IDomainRepository<InvoiceDomain, Invoice>>();
        InvoiceDomain owner = await invoiceDomains.GetAsync(biller, invoice, forceLoad: true);
        await owner.FreezeAsync(request.InvoiceItems.ToArray(), "synthetic-integration-salt", default);

        InvoiceNotificationDeliveryConsumer consumer = new(host.Services.GetRequiredService<InvoiceNotificationDeliveryFlow>());
        DateTimeOffset at = DateTimeOffset.UtcNow;
        await Receive(consumer, Receipt(request, NotificationDeliveryKind.Submitted, "submitted", at));

        var receiptDomains = host.Services.GetRequiredService<IDomainRepository<InvoiceNotificationDeliveryDomain, InvoiceNotificationDelivery>>();
        InvoiceNotificationDeliveryDomain first = await receiptDomains.GetAsync(biller, invoice, forceLoad: true);
        InvoiceNotificationDeliveryDomain stale = await receiptDomains.GetAsync(biller, invoice, forceLoad: true);
        await first.GetEntityAsync();
        await stale.GetEntityAsync();

        NotificationDeliveryEvent delivered = Receipt(request, NotificationDeliveryKind.Delivered, "delivered", at.AddSeconds(2));
        NotificationDeliveryEvent delayed = Receipt(request, NotificationDeliveryKind.Delayed, "delayed", at.AddSeconds(1));
        await first.RecordAsync(delivered, request.BillerID, request.InvoiceID, default);
        RequestFailedException conflict = await Assert.ThrowsExactlyAsync<RequestFailedException>(() =>
            stale.RecordAsync(delayed, request.BillerID, request.InvoiceID, default));
        Assert.AreEqual(412, conflict.Status);
        await Receive(consumer, delayed);

        InvoiceLifecycleStatus status = await host.Services.GetRequiredService<InvoiceLifecycleFlow>()
            .GetAsync(request.BillerID, request.InvoiceID, request.Tenant);
        Assert.AreEqual(InvoiceDeliveryState.Delivered, status.Delivery);
        Assert.AreEqual<DateTimeOffset?>(delivered.OccurredAt, status.ConfirmedDeliveredAt);
        Assert.AreEqual<DateTimeOffset?>(delayed.OccurredAt, status.Notification?.LastDelayedAt);
        Assert.IsEmpty(messages);
    }

    private static NotificationDeliveryEvent Receipt(IssueInvoiceRequest request, NotificationDeliveryKind kind,
        string id, DateTimeOffset at) => new()
    {
        ID = "resend/" + id,
        NotificationID = Invoice.BuildFullID(request.BillerID, request.InvoiceID),
        Channel = "Invoice",
        Tenant = request.BillerID,
        SubmissionID = "original-invoice-submission",
        Provider = "resend",
        ProviderEventID = id,
        ProviderMessageID = "provider-message",
        Kind = kind,
        OccurredAt = at,
    };

    private static Task Receive(InvoiceNotificationDeliveryConsumer consumer, NotificationDeliveryEvent receipt, string? envelopeID = null)
    {
        MessagingEntry<NotificationDeliveryEvent> entry = new() { ID = receipt.ID, Value = receipt };
        ServiceBusReceivedMessage message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(entry.Body), messageId: envelopeID ?? receipt.ID, contentType: "application/json");
        return consumer.Run(message, default);
    }
}
