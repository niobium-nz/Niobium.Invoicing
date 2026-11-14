using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Domains;

public partial class InvoiceDomain
{
    /// <summary>Reads one verified committed snapshot without scanning or changing storage.</summary>
    public async Task<InvoiceHistoryPage> ReadHistoryAsync(Guid tenant, Guid actor, string? continuation = null,
        CancellationToken cancellationToken = default)
    {
        Invoice invoice = await GetEntityAsync(cancellationToken);
        VerifyOwner(invoice, tenant, actor);
        Guid? revision = Lifecycle(invoice).HistoryRevision;
        DateTimeOffset now = clock.GetUtcNow();
        if (continuation != null)
        {
            bool valid = InvoiceHistoryCursor.TryResolve(continuation, invoice, now, config.Value.InvoiceTokenSecretSalt, out Guid next);
            if (!valid)
                throw BadRequest("The invoice history continuation is invalid or expired. Start from the latest history.");
            revision = next;
        }
        if (!revision.HasValue)
            return new(null, null);
        InvoiceHistoryDomain domain = await LoadRevisionAsync(invoice, revision.Value, cancellationToken);
        InvoiceRevision stored = await domain.GetEntityAsync(cancellationToken);
        InvoiceRevisionSnapshot snapshot = await domain.ReadSnapshotAsync(cancellationToken);
        string? cursor = stored.Previous.HasValue
            ? InvoiceHistoryCursor.Create(invoice, stored.Previous.Value, now, config.Value.InvoiceTokenSecretSalt)
            : null;
        return new(new(stored.ID, stored.ReadChange(), snapshot), cursor);
    }
}
