using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Payments;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    public async Task Actual_adapter_maps_business_payment_keys_and_preserves_the_immutable_receipt()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        Invoice invoice = (await host.Services.GetRequiredService<IRepository<Invoice>>().RetrieveAsync(
            Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        InvoicePaymentEntry entry = new(Guid.NewGuid(), InvoicePaymentKind.Received, 12345, "NZD", null,
            DateTimeOffset.UtcNow.AddHours(-1).AddTicks(1234), request.BillerID,
            "Synthetic manual receipt", "Synthetic evidence reference", DateTimeOffset.UtcNow);
        InvoicePayment payment = InvoicePayment.Capture(invoice, entry);
        IRepository<InvoicePayment> repository = host.Services.GetRequiredService<IRepository<InvoicePayment>>();
        await repository.CreateAsync([payment], replaceIfExist: false);
        StorageKey key = payment.GetKey();
        InvoicePayment stored = (await repository.RetrieveAsync(key.PartitionKey, key.RowKey))!;
        Assert.AreEqual(entry, stored.Read());
        Assert.IsFalse(String.IsNullOrWhiteSpace(stored.ETag));
        TableEntity raw = (await tableService!.GetTableClient(prefix + nameof(InvoicePayment)).GetEntityAsync<TableEntity>(
            key.PartitionKey, key.RowKey)).Value;
        Assert.AreEqual(invoice.Biller.ToString(), raw.PartitionKey);
        Assert.AreEqual(payment.ID, raw.RowKey);
        Assert.IsFalse(raw.ContainsKey(nameof(InvoicePayment.Biller)));
        Assert.IsFalse(raw.ContainsKey(nameof(InvoicePayment.ID)));

        InvoicePayment conflicting = InvoicePayment.Capture(invoice, entry with { AmountCents = 1 });
        RequestFailedException duplicate = await Assert.ThrowsExactlyAsync<RequestFailedException>(
            () => repository.CreateAsync([conflicting], replaceIfExist: false));
        Assert.AreEqual(409, duplicate.Status);
        InvoicePayment unchanged = (await repository.RetrieveAsync(key.PartitionKey, key.RowKey))!;
        Assert.AreEqual(stored.ETag, unchanged.ETag);
        Assert.AreEqual(entry, unchanged.Read());
    }

    [TestMethod]
    public async Task Actual_adapter_roundtrips_rebased_postings_without_updating_the_receipt_or_predecessor()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        IRepository<Invoice> invoices = host.Services.GetRequiredService<IRepository<Invoice>>();
        Invoice invoice = (await invoices.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        InvoicePaymentEntry a = new(Guid.NewGuid(), InvoicePaymentKind.Received, 1000, "NZD", null,
            DateTimeOffset.UtcNow.AddMinutes(-1), request.BillerID, "Synthetic receipt A", "Synthetic evidence A", DateTimeOffset.UtcNow);
        InvoicePayment firstPayment = InvoicePayment.Capture(invoice, a);
        InvoicePayment secondPayment = InvoicePayment.Capture(invoice, a with { Operation = Guid.NewGuid(), AmountCents = 250 });
        IRepository<InvoicePayment> payments = host.Services.GetRequiredService<IRepository<InvoicePayment>>();
        IRepository<InvoicePaymentPosting> postings = host.Services.GetRequiredService<IRepository<InvoicePaymentPosting>>();
        await payments.CreateAsync([firstPayment, secondPayment], replaceIfExist: false);
        InvoicePaymentPosting first = InvoicePaymentPosting.Capture(firstPayment, null, 0, 1000, "v1-" + new string('a', 64));
        InvoicePaymentPosting abandoned = InvoicePaymentPosting.Capture(secondPayment, null, 0, 250, "v1-" + new string('a', 64));
        InvoicePaymentPosting rebased = InvoicePaymentPosting.Capture(secondPayment, first.ReadPosition(), 0, 1250, "v1-" + new string('a', 64));
        await postings.CreateAsync([first, abandoned, rebased], replaceIfExist: false);
        InvoicePaymentPosting actual = (await postings.RetrieveAsync(rebased.GetKey().PartitionKey, rebased.GetKey().RowKey))!;
        Assert.AreEqual(rebased.ReadPosition(), actual.ReadPosition());
        Assert.IsTrue(actual.References(secondPayment));
        Assert.AreEqual(first.GetKey(), actual.GetPreviousKey());
        Assert.AreNotEqual(abandoned.ID, actual.ID);
        Invoice after = (await invoices.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        Assert.AreEqual(invoice.ETag, after.ETag, "Candidate payment records cannot commit themselves to the invoice.");
        Assert.AreEqual(invoice.LifecycleData, after.LifecycleData);
    }
}
