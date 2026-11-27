namespace Niobium.Invoicing.Domains;

public partial class InvoiceDomain
{
    internal async Task VerifyNotificationOperationAsync(string operation, Guid biller, CancellationToken cancellationToken)
    {
        Invoice invoice = await GetEntityAsync(cancellationToken);
        if (invoice.Biller != biller || invoice.GetFullID() != operation
            || invoice.GetPreparationStage() != InvoicePreparationStage.Frozen
            || String.IsNullOrWhiteSpace(invoice.Token)
            || String.IsNullOrWhiteSpace(invoice.IssuanceNotificationEndpoint))
            throw Conflict("The receipt does not match the frozen invoice notification.");
    }
}
