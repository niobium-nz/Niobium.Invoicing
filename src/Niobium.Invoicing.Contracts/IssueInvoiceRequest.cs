using Niobium.Finance;

namespace Niobium.Invoicing
{
    /// <summary>
    /// Input for issuing or reissuing an invoice; transport-specific checks are applied by its caller.
    /// </summary>
    public class IssueInvoiceRequest
    {
        /// <summary>
        /// Consumer-supplied reverse Unix millisecond identifier; HTTP issuance checks its decoded issue time
        /// against a 30-minute age limit.
        /// </summary>
        public long InvoiceID { get; set; }

        /// <summary>
        /// Invoicing management group; HTTP issuance requires equality with the GroupSid claim.
        /// </summary>
        public required Guid Tenant { get; set; }

        /// <summary>
        /// Issuing business; HTTP issuance requires equality with the Sid claim.
        /// </summary>
        public required Guid BillerID { get; set; }

        /// <summary>
        /// Customer row identifier used when no embedded billee is supplied.
        /// </summary>
        public required Guid BilleeID { get; set; }

        /// <summary>
        /// Optional requested payment particulars; current update flow does not apply this field.
        /// </summary>
        public string? Particulars { get; set; }

        /// <summary>
        /// Optional requested payment reference; current update flow does not apply this field.
        /// </summary>
        public string? Reference { get; set; }

        /// <summary>
        /// Numeric InvoiceCycle used for billing-period display, not scheduling.
        /// </summary>
        public int InvoiceCycle { get; set; }

        /// <summary>
        /// Optional billing-period start instant copied to the invoice.
        /// </summary>
        public DateTimeOffset? BillingPeriodStartDay { get; set; }

        /// <summary>
        /// Optional custom-range end instant copied to the invoice.
        /// </summary>
        public DateTimeOffset? BillingPeriodEndDay { get; set; }

        /// <summary>
        /// Requested due instant; current update copies null as well as non-null values, replacing the
        /// seven-day default.
        /// </summary>
        public DateTimeOffset? DueBy { get; set; }

        /// <summary>
        /// Optional invoice terms trimmed on update.
        /// </summary>
        public string? Terms { get; set; }

        /// <summary>
        /// Optional payment instructions trimmed on update; null replaces the biller default.
        /// </summary>
        public string? PaymentInstructions { get; set; }

        /// <summary>
        /// Supplied replacement line collection; the flow does not rewrite each item parent key.
        /// </summary>
        public required List<InvoiceItem> InvoiceItems { get; set; }

        /// <summary>
        /// Reported settlement amount, default Amount.Zero; update copies cents without checking currency.
        /// </summary>
        public Amount Settled { get; set; } = Amount.Zero;

        /// <summary>
        /// Whether IssueFlow should enqueue notification after persistence; the HTTP endpoint always resets
        /// this to false.
        /// </summary>
        public bool NotifyBillee { get; set; }
    }
}
