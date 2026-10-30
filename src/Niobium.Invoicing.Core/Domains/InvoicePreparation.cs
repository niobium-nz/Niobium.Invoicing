using Azure;
using System.Net;

namespace Niobium.Invoicing.Domains;

public partial class InvoiceDomain
{
    /// <summary>Claims preparation, or recognizes an exact replay without rewriting the aggregate.</summary>
    public async Task<bool> UpdateAsync(IssueInvoiceRequest update, IEnumerable<InvoiceItem> existingItems,
        CancellationToken cancellationToken, Billee? customer = null)
    {
        Invoice current = await GetEntityAsync(cancellationToken);
        CheckState(current);
        if (current.IssuanceState == InvoiceContent.Preparing)
        {
            throw Conflict("Invoice preparation is already in progress; a crashed writer requires recovery.");
        }

        Invoice candidate = InvoiceContent.Copy(current);
        if (candidate.IssuanceState == null)
        {
            // Legacy stored values are the only available historical defaults; do not fetch new profile values.
            candidate.IssuanceDefaultParticulars = current.Particulars;
            candidate.IssuanceDefaultReference = current.Reference;
            candidate.IssuanceDefaultPaymentInstructions = current.PaymentInstructions;
        }
        if (customer != null)
        {
            ApplyCustomer(candidate, customer);
        }
        InvoiceItem[] items = update.InvoiceItems.ToArray();
        InvoiceValidation.Totals totals = InvoiceValidation.ValidateAndCalculate(update, candidate, items);
        foreach (InvoiceItem item in items)
        {
            item.LineTotalCents = item.FigureLineTotalCents();
        }
        candidate.Terms = update.Terms?.Trim();
        candidate.Particulars = update.Particulars?.Trim() ?? candidate.IssuanceDefaultParticulars;
        candidate.Reference = update.Reference?.Trim() ?? candidate.IssuanceDefaultReference;
        candidate.PaymentInstructions = update.PaymentInstructions?.Trim() ?? candidate.IssuanceDefaultPaymentInstructions;
        candidate.InvoiceCycle = update.InvoiceCycle;
        candidate.BillingPeriodStartDay = update.BillingPeriodStartDay;
        candidate.BillingPeriodEndDay = update.BillingPeriodEndDay;
        candidate.DueBy = totals.DueBy;
        candidate.SubtotalCents = totals.Subtotal;
        candidate.TaxCents = totals.Tax;
        candidate.GrandTotalCents = totals.GrandTotal;
        candidate.SettledCents = update.Settled.Cents;

        // A replay does not migrate an already-frozen historical invoice that lacks a due date.
        if (InvoiceContent.IsFrozen(current) && update.DueBy == null && current.DueBy == null)
        {
            candidate.DueBy = null;
        }
        string requested = InvoiceContent.Fingerprint(candidate, items);
        string persisted = InvoiceContent.Fingerprint(current, existingItems);
        if (InvoiceContent.IsFrozen(current))
        {
            if (requested != persisted || (current.IssuanceFingerprint != null && persisted != current.IssuanceFingerprint))
            {
                throw Conflict("Notification content is frozen; only an exact replay is allowed.");
            }
            return false;
        }
        if (current.IssuanceState == InvoiceContent.Retryable && requested != current.IssuanceFingerprint)
        {
            throw Conflict("Incomplete item preparation must be recovered with the same content.");
        }
        if (current.IssuanceState == InvoiceContent.Ready && requested == current.IssuanceFingerprint && persisted == requested)
        {
            return false;
        }

        candidate.IssuanceState = InvoiceContent.Preparing;
        candidate.IssuanceFingerprint = requested;
        candidate.IssuanceOperation = Guid.NewGuid().ToString("N");
        await PersistInvoiceAsync(candidate, cancellationToken);
        return true;
    }

    /// <summary>Marks successful item replacement ready, retaining its content fingerprint.</summary>
    public async Task CompletePreparationAsync(IEnumerable<InvoiceItem> persistedItems, CancellationToken cancellationToken)
    {
        Invoice current = await GetEntityAsync(cancellationToken);
        if (InvoiceContent.Fingerprint(current, persistedItems) != current.IssuanceFingerprint)
        {
            throw Conflict("Persisted items do not match the preparation checkpoint.");
        }
        await EndPreparationAsync(InvoiceContent.Ready, cancellationToken);
    }

    /// <summary>Allows an exact retry after an item operation has returned a definitive failure.</summary>
    public Task FailPreparationAsync(CancellationToken cancellationToken) => EndPreparationAsync(InvoiceContent.Retryable, cancellationToken);

    private async Task EndPreparationAsync(string state, CancellationToken cancellationToken)
    {
        Invoice current = await GetEntityAsync(cancellationToken);
        if (current.IssuanceState != InvoiceContent.Preparing || string.IsNullOrWhiteSpace(current.IssuanceOperation))
        {
            throw Conflict("The preparation claim is no longer held.");
        }
        Invoice next = InvoiceContent.Copy(current);
        next.IssuanceState = state;
        next.IssuanceOperation = null;
        await PersistInvoiceAsync(next, cancellationToken);
    }

    /// <summary>Freezes verified persisted content and saves its link before any enqueue attempt.</summary>
    public async Task<string> FreezeAsync(IReadOnlyList<InvoiceItem> items, string salt, CancellationToken cancellationToken)
    {
        Invoice current = await GetEntityAsync(cancellationToken);
        CheckState(current);
        if (!InvoiceContent.IsFrozen(current) && current.IssuanceState != InvoiceContent.Ready)
        {
            throw Conflict("Invoice items have not completed preparation.");
        }
        string fingerprint = InvoiceContent.Fingerprint(current, items);
        if (current.IssuanceFingerprint != null && current.IssuanceFingerprint != fingerprint)
        {
            throw Conflict("Persisted invoice items do not match the prepared content.");
        }
        if (current.IssuanceState == InvoiceContent.Frozen && !string.IsNullOrWhiteSpace(current.Token) &&
            !string.IsNullOrWhiteSpace(current.IssuanceNotificationEndpoint))
        {
            return current.Token;
        }
        Invoice next = InvoiceContent.Copy(current);
        next.Token = string.IsNullOrWhiteSpace(current.Token) ? current.BuildAccessToken(salt) : current.Token;
        next.IssuanceState = InvoiceContent.Frozen;
        next.IssuanceFingerprint = fingerprint;
        next.IssuanceOperation = null;
        if (current.IssuanceState == null)
        {
            next.IssuanceDefaultParticulars = current.Particulars;
            next.IssuanceDefaultReference = current.Reference;
            next.IssuanceDefaultPaymentInstructions = current.PaymentInstructions;
        }
        next.IssuanceNotificationEndpoint ??= config.Value.GetInvoiceEndpoint;
        if (string.IsNullOrWhiteSpace(next.IssuanceNotificationEndpoint))
        {
            throw new InvalidOperationException("An invoice link endpoint is required before freezing notification content.");
        }
        await PersistInvoiceAsync(next, cancellationToken);
        return next.Token;
    }

    private async Task PersistInvoiceAsync(Invoice next, CancellationToken cancellationToken)
    {
        try
        {
            await SaveAsync([next], cancellationToken: cancellationToken);
            Invoice saved = await GetEntityAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(saved.ETag))
            {
                // Niobium 4.0.60 loses response ETags on writes. Reads retain them. Re-read and
                // reject intervening changes instead of blindly treating a new version as our own.
                await ReloadAsync(cancellationToken);
                saved = await GetEntityAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(saved.ETag) || !InvoiceContent.SamePersistedContent(next, saved))
                {
                    throw Conflict("Invoice changed while refreshing its persistence version.");
                }
            }
        }
        catch (RequestFailedException error) when (error.Status is 409 or 412)
        {
            throw Conflict("Invoice was changed by another request.");
        }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            throw Conflict("Invoice was changed by another request.");
        }
    }

    private static void CheckState(Invoice invoice)
    {
        if (invoice.IssuanceState is not (null or InvoiceContent.Preparing or InvoiceContent.Retryable or InvoiceContent.Ready or InvoiceContent.Frozen))
        {
            throw Conflict("Invoice preparation version is not supported.");
        }
    }

    private static void ApplyCustomer(Invoice invoice, Billee customer)
    {
        bool changedIdentity = invoice.Billee != customer.ID;
        invoice.Billee = customer.ID;
        invoice.BilleeName = customer.Name;
        invoice.BilleeBusinessID = customer.BusinessID;
        invoice.BilleeAddressLine1 = customer.AddressLine1;
        invoice.BilleeAddressLine2 = customer.AddressLine2;
        invoice.BilleeAddressSuburb = customer.Suburb;
        invoice.BilleeAddressCity = customer.City;
        invoice.BilleeAddressState = customer.State;
        invoice.BilleeAddressCountry = customer.Country;
        invoice.BilleeAddressZipcode = customer.Zipcode;
        invoice.RecipientEmail = customer.Email;
        invoice.Culture = customer.Culture;
        invoice.TimeZone = customer.TimeZone;
        if (changedIdentity)
        {
            string particulars = customer.Name.ToUpperInvariant().Replace("LIMITED", string.Empty).Replace(" ", string.Empty);
            invoice.IssuanceDefaultParticulars = particulars[..Math.Min(12, particulars.Length)];
        }
    }

    private static ApplicationException Conflict(string message) => new(InternalError.Conflict, message);
}
