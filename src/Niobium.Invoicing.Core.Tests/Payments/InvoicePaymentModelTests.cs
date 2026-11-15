using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.Payments;

namespace Niobium.Invoicing.Core.Tests.Payments;

[TestClass]
public sealed class InvoicePaymentModelTests
{
    [TestMethod]
    public void Receipt_identity_is_stable_for_retry_but_scoped_to_biller_invoice_and_operation()
    {
        Invoice invoice = Invoice();
        InvoicePaymentEntry entry = Entry(invoice);
        InvoicePayment first = InvoicePayment.Capture(invoice, entry);
        InvoicePayment retry = InvoicePayment.Capture(invoice, entry with
        {
            RecordedAt = entry.RecordedAt.AddMinutes(1)
        });
        Assert.AreEqual(first.ID, retry.ID);
        Assert.AreNotEqual(first.ContentDigest, retry.ContentDigest);
        Assert.IsTrue(first.Matches(retry.Read()));
        Assert.IsFalse(first.Matches(entry with
        {
            AmountCents = entry.AmountCents + 1
        }));
        Assert.IsFalse(first.Matches(entry with
        {
            Evidence = "another synthetic bank reference"
        }));
        Assert.IsFalse(first.Matches(entry with
        {
            OccurredAt = entry.OccurredAt.AddSeconds(1)
        }));
        Assert.AreNotEqual(first.GetKey().RowKey, InvoicePayment.Key(Guid.NewGuid(), invoice.GetID(), entry.Operation).RowKey);
        Assert.AreNotEqual(first.GetKey().RowKey, InvoicePayment.Key(invoice.Biller, invoice.GetID() + 1, entry.Operation).RowKey);
        Assert.AreNotEqual(first.GetKey().RowKey, InvoicePayment.Key(invoice.Biller, invoice.GetID(), Guid.NewGuid()).RowKey);
        Assert.IsTrue(first.Describes(invoice));
        invoice.Tenant = Guid.NewGuid();
        Assert.IsFalse(first.Describes(invoice));
    }

    [TestMethod]
    public void Equal_instants_with_different_offsets_keep_the_same_normalized_receipt()
    {
        Invoice invoice = Invoice();
        InvoicePaymentEntry entry = Entry(invoice);
        InvoicePayment first = InvoicePayment.Capture(invoice, entry);
        InvoicePayment second = InvoicePayment.Capture(invoice, entry with
        {
            OccurredAt = entry.OccurredAt.ToOffset(TimeSpan.FromHours(13)),
            RecordedAt = entry.RecordedAt.ToOffset(TimeSpan.FromHours(-4))
        });
        Assert.AreEqual(first.ContentDigest, second.ContentDigest);
        Assert.AreEqual(entry, second.Read());
    }

    [TestMethod]
    [DataRow("AmountCents")]
    [DataRow("Evidence")]
    [DataRow("Tenant")]
    [DataRow("Version")]
    [DataRow("ID")]
    [DataRow("RecordedAt")]
    public void Changed_receipt_columns_are_not_accepted_as_original_evidence(string property)
    {
        Invoice invoice = Invoice();
        InvoicePayment payment = InvoicePayment.Capture(invoice, Entry(invoice));
        object value = property switch
        {
            "AmountCents" => payment.AmountCents + 1,
            "Evidence" => "changed evidence",
            "Tenant" => Guid.NewGuid(),
            "Version" => 2,
            "ID" => payment.ID[..^1] + (payment.ID[^1] == 'a' ? "b" : "a"),
            _ => payment.RecordedAt.AddSeconds(1)
        };
        typeof(InvoicePayment).GetProperty(property)!.SetValue(payment, value);
        Assert.ThrowsExactly<InvalidOperationException>(() => payment.Read());
    }

    [TestMethod]
    public void Rebase_has_a_different_posting_identity_without_changing_the_operation_receipt()
    {
        Invoice invoice = Invoice();
        InvoicePayment a = InvoicePayment.Capture(invoice, Entry(invoice));
        InvoicePayment b = InvoicePayment.Capture(invoice, Entry(invoice) with
        {
            AmountCents = 250
        });
        InvoicePaymentPosting first = InvoicePaymentPosting.Capture(a, null, 100, 1100, "v1-" + new string('a', 64));
        InvoicePaymentPosting abandoned = InvoicePaymentPosting.Capture(b, null, 100, 350, "v1-" + new string('a', 64));
        InvoicePaymentPosting rebased = InvoicePaymentPosting.Capture(b, first.ReadPosition(), 100, 1350, "v1-" + new string('a', 64));
        Assert.AreNotEqual(abandoned.ID, rebased.ID);
        Assert.AreEqual(abandoned.Payment, rebased.Payment);
        Assert.AreEqual(abandoned.PaymentDigest, rebased.PaymentDigest);
        Assert.AreEqual(first.GetKey(), rebased.GetPreviousKey());
        Assert.AreEqual(b.GetKey(), rebased.GetPaymentKey());
        Assert.AreEqual(2, rebased.ReadPosition().Sequence);
        Assert.AreEqual(1350, rebased.ReadPosition().SettledCents);
        Assert.IsTrue(rebased.References(b));
        Assert.IsFalse(rebased.References(a));
        Assert.IsTrue(rebased.Describes(invoice));
        invoice.Tenant = Guid.NewGuid();
        Assert.IsFalse(rebased.Describes(invoice));
    }

    [TestMethod]
    [DataRow("SettledCents")]
    [DataRow("OpeningSettledCents")]
    [DataRow("PaymentDigest")]
    [DataRow("Previous")]
    [DataRow("Sequence")]
    [DataRow("Currency")]
    public void Posting_identity_detects_changed_balance_and_chain_evidence(string property)
    {
        Invoice invoice = Invoice();
        InvoicePayment payment = InvoicePayment.Capture(invoice, Entry(invoice));
        InvoicePaymentPosting posting = InvoicePaymentPosting.Capture(payment, null, 0, payment.AmountCents, "v1-" + new string('a', 64));
        object value = property switch
        {
            "SettledCents" => 1L,
            "OpeningSettledCents" => 1L,
            "Sequence" => 2L,
            "PaymentDigest" => new String('a', 64),
            "Previous" => posting.ID,
            _ => "AUD"
        };
        typeof(InvoicePaymentPosting).GetProperty(property)!.SetValue(posting, value);
        Assert.ThrowsExactly<InvalidOperationException>(() => posting.ReadPosition());
    }

    [TestMethod]
    public void Reversal_keeps_its_referenced_receipt_and_has_its_own_operation_identity()
    {
        Invoice invoice = Invoice();
        InvoicePaymentEntry received = Entry(invoice);
        InvoicePaymentEntry reversal = Entry(invoice) with
        {
            Kind = InvoicePaymentKind.Reversed,
            Reverses = received.Operation
        };
        InvoicePayment stored = InvoicePayment.Capture(invoice, reversal);
        Assert.AreEqual(reversal, stored.Read());
        Assert.AreNotEqual(InvoicePayment.Capture(invoice, received).ID, stored.ID);
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoicePayment.Capture(invoice, reversal with { Reverses = reversal.Operation }));
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoicePayment.Capture(invoice, received with { Reverses = Guid.NewGuid() }));
    }

    private static Invoice Invoice()
    {
        IssuanceScenario scenario = new();
        return Niobium.Invoicing.Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
    }

    private static InvoicePaymentEntry Entry(Invoice invoice)
        => new(Guid.NewGuid(), InvoicePaymentKind.Received, 1000, "NZD", null,
            new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero), invoice.Biller,
            "Manual bank receipt", "synthetic-bank-reference", new DateTimeOffset(2026, 11, 14, 11, 0, 0, TimeSpan.Zero));
}
