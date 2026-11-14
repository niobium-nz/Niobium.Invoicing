using Azure;
using System.Net;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Domains;

public partial class InvoiceDomain
{
    /// <summary>Claims preparation, or recognizes an exact replay without rewriting the aggregate.</summary>
    public async Task<bool> UpdateAsync(IssueInvoiceRequest update, IEnumerable<InvoiceItem> existingItems,
        CancellationToken cancellationToken, Billee? customer = null, bool newInvoice = false)
    {
        Invoice current = await GetEntityAsync(cancellationToken);
        CheckState(current);
        CheckActive(current);
        if (current.GetPreparationStage() == InvoicePreparationStage.Preparing)
        {
            throw Conflict("Invoice preparation is already in progress; a crashed writer requires recovery.");
        }

        InvoiceItem[] existing = existingItems.ToArray();
        RequireHistoryCapacity(current, existing);
        Invoice candidate = InvoiceContent.Copy(current);
        if (candidate.GetPreparationStage() == InvoicePreparationStage.Draft)
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
        RequireHistoryCapacity(candidate, items, requested: true);
        string requested = InvoiceContent.Fingerprint(candidate, items);
        string persisted = InvoiceContent.Fingerprint(current, existing);
        if (InvoiceContent.IsFrozen(current))
        {
            if (requested != persisted || (current.IssuanceFingerprint != null && persisted != current.IssuanceFingerprint))
            {
                throw Conflict("Notification content is frozen; only an exact replay is allowed.");
            }
            return false;
        }
        if (current.GetPreparationStage() == InvoicePreparationStage.Retryable && requested != current.IssuanceFingerprint)
        {
            throw Conflict("Incomplete item preparation must be recovered with the same content.");
        }
        if (current.GetPreparationStage() == InvoicePreparationStage.Ready && requested == current.IssuanceFingerprint && persisted == requested)
        {
            return false;
        }

        InvoiceLifecycleSnapshot lifecycle = Lifecycle(current);
        if (newInvoice)
        {
            if (!String.IsNullOrWhiteSpace(current.ETag) || current.Timestamp != null || current.IssuanceState != null)
                throw Conflict("An existing invoice cannot be classified as a new payment history.");
            lifecycle = lifecycle with
            {
                PaymentHistory = InvoicePaymentHistory.NoPayments,
                Delivery = InvoiceDeliveryState.NotRequested
            };
        }
        if (current.SettledCents != 0 || candidate.SettledCents != 0)
            lifecycle = lifecycle with
            {
                PaymentHistory = InvoicePaymentHistory.PaymentOccurred,
                PaymentObservedAt = lifecycle.PaymentObservedAt ?? DateTimeOffset.UtcNow
            };
        candidate.StoreLifecycle(lifecycle with
        {
            Preparation = InvoicePreparationStage.Preparing
        });
        candidate.IssuanceFingerprint = requested;
        candidate.IssuanceOperation = Guid.NewGuid().ToString("N");
        await PersistWithHistoryAsync(candidate, InvoiceChangeKind.PreparationStarted, "Invoice preparation requested",
            items, InvoiceLineEvidence.Proposed, cancellationToken: cancellationToken);
        return true;
    }

    /// <summary>Marks successful item replacement ready, retaining its content fingerprint.</summary>
    public async Task CompletePreparationAsync(IEnumerable<InvoiceItem> persistedItems, CancellationToken cancellationToken)
    {
        Invoice current = await GetEntityAsync(cancellationToken);
        InvoiceItem[] items = persistedItems.ToArray();
        RequireHistoryCapacity(current, items);
        if (InvoiceContent.Fingerprint(current, items) != current.IssuanceFingerprint)
        {
            throw Conflict("Persisted items do not match the preparation checkpoint.");
        }
        await EndPreparationAsync(InvoicePreparationStage.Ready, cancellationToken, items);
    }

    /// <summary>Allows an exact retry after an item operation has returned a definitive failure.</summary>
    public Task FailPreparationAsync(CancellationToken cancellationToken) => EndPreparationAsync(InvoicePreparationStage.Retryable, cancellationToken);

    private async Task EndPreparationAsync(InvoicePreparationStage state, CancellationToken cancellationToken,
        IReadOnlyList<InvoiceItem>? items = null)
    {
        Invoice current = await GetEntityAsync(cancellationToken);
        if (current.GetPreparationStage() != InvoicePreparationStage.Preparing || string.IsNullOrWhiteSpace(current.IssuanceOperation))
        {
            throw Conflict("The preparation claim is no longer held.");
        }
        Invoice next = InvoiceContent.Copy(current);
        CheckActive(current);
        next.SetPreparationStage(state);
        next.IssuanceOperation = null;
        await PersistWithHistoryAsync(next, state == InvoicePreparationStage.Ready
            ? InvoiceChangeKind.PreparationCompleted : InvoiceChangeKind.PreparationFailed,
            state == InvoicePreparationStage.Ready ? "Invoice preparation completed" : "Invoice preparation failed with a confirmed storage rejection",
            items, items == null ? InvoiceLineEvidence.Proposed : InvoiceLineEvidence.Verified, cancellationToken: cancellationToken);
    }

    /// <summary>Freezes verified persisted content and saves its link before any enqueue attempt.</summary>
    public async Task<string> FreezeAsync(IReadOnlyList<InvoiceItem> items, string salt, CancellationToken cancellationToken)
    {
        Invoice current = await GetEntityAsync(cancellationToken);
        CheckState(current);
        CheckActive(current);
        if (!InvoiceContent.IsFrozen(current) && current.GetPreparationStage() != InvoicePreparationStage.Ready)
        {
            throw Conflict("Invoice items have not completed preparation.");
        }
        RequireHistoryCapacity(current, items);
        string fingerprint = InvoiceContent.Fingerprint(current, items);
        if (current.IssuanceFingerprint != null && current.IssuanceFingerprint != fingerprint)
        {
            throw Conflict("Persisted invoice items do not match the prepared content.");
        }
        if (current.GetPreparationStage() == InvoicePreparationStage.Frozen && !string.IsNullOrWhiteSpace(current.Token) &&
            !string.IsNullOrWhiteSpace(current.IssuanceNotificationEndpoint))
        {
            return current.Token;
        }
        Invoice next = InvoiceContent.Copy(current);
        next.Token = string.IsNullOrWhiteSpace(current.Token) ? current.BuildAccessToken(salt) : current.Token;
        InvoiceLifecycleSnapshot lifecycle = Lifecycle(current);
        next.StoreLifecycle(lifecycle with
        {
            Preparation = InvoicePreparationStage.Frozen,
            Delivery = lifecycle.ConfirmedDeliveredAt == null ? InvoiceDeliveryState.Delivering : InvoiceDeliveryState.Delivered,
            NotificationRequestedAt = lifecycle.NotificationRequestedAt ?? DateTimeOffset.UtcNow,
        });
        next.IssuanceFingerprint = fingerprint;
        next.IssuanceOperation = null;
        if (current.GetPreparationStage() == InvoicePreparationStage.Draft)
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
        await PersistWithHistoryAsync(next, InvoiceChangeKind.Frozen, "Invoice content frozen for notification",
            items, InvoiceLineEvidence.Verified, cancellationToken: cancellationToken);
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
        _ = Lifecycle(invoice);
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
