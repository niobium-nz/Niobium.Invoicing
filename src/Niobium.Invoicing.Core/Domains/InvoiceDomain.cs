using Microsoft.Extensions.Options;
using Niobium.Invoicing.Options;
using System.Globalization;

namespace Niobium.Invoicing.Domains;

/// <summary>
/// Updates and renders an invoice through the Niobium domain/repository abstraction.
/// </summary>
/// <param name="config">Bound billing configuration for invoice links.</param>
/// <param name="repo">Lazy storage repository for loading and saving invoice entities.</param>
/// <param name="eventHandlers">Domain event handlers passed to the base Niobium domain.</param>
public partial class InvoiceDomain(
    IOptions<BillingOptions> config,
    Lazy<IRepository<Invoice>> repo,
    IEnumerable<IDomainEventHandler<IDomain<Invoice>>> eventHandlers)
      : GenericDomain<Invoice>(repo, eventHandlers)
{
    /// <summary>
    /// Process-wide cached invoice template text.
    /// </summary>
    private static string? htmlTemplate;

    /// <summary>
    /// Applies mutable request fields, recalculates totals and force-saves the loaded entity.
    /// </summary>
    /// <param name="update">Requested mutable fields and reported settlement.</param>
    /// <param name="invoiceItems">Supplied lines whose totals are recalculated in memory.</param>
    /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
    /// <returns>A task completing after the invoice save.</returns>
    /// <remarks>
    /// The Delivered guard applies to the loaded entity; UpsertFlow initializes that entity from a fresh
    /// snapshot. Supplied line totals are mutated in memory; line persistence is the caller's
    /// responsibility. Force-save uses repository create/upsert semantics, without an ETag update
    /// precondition. Particulars and Reference are not applied; DueBy and PaymentInstructions may be
    /// overwritten with null.
    /// </remarks>
    /// <exception cref="Niobium.ApplicationException">The loaded entity has a Delivered timestamp.</exception>
    public async Task UpdateAsync(IssueInvoiceRequest update, IEnumerable<InvoiceItem> invoiceItems, CancellationToken cancellationToken)
    {
        Invoice entity = await GetEntityAsync(cancellationToken);
        if (entity.Delivered.HasValue)
        {
            throw new ApplicationException(InternalError.Conflict, "Invoice has been delivered.") { Reference = entity.GetFullID() };
        }

        foreach (InvoiceItem item in invoiceItems)
        {
            item.LineTotalCents = item.FigureLineTotalCents();
        }

        entity.Terms = update.Terms?.Trim();
        entity.PaymentInstructions = update.PaymentInstructions?.Trim();
        entity.InvoiceCycle = update.InvoiceCycle;
        entity.BillingPeriodStartDay = update.BillingPeriodStartDay;
        entity.BillingPeriodEndDay = update.BillingPeriodEndDay;
        entity.DueBy = update.DueBy;
        entity.SubtotalCents = invoiceItems.FigureSubTotalCents();
        entity.TaxCents = entity.FigureTaxTotalCents(invoiceItems);
        entity.GrandTotalCents = entity.FigureGrandTotalCents(invoiceItems);
        entity.SettledCents = update.Settled.Cents;

        await SaveAsync(force: true, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Renders the invoice-level HTML using its stored link-token policy.
    /// </summary>
    /// <param name="token">Supplied invoice-link token.</param>
    /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
    /// <returns>HTML containing the invoice-item template block for later replacement.</returns>
    /// <remarks>
    /// Loads and caches the embedded template; the stored Template selector is not used. This method does
    /// not establish a recipient principal or an expiry policy. A saved token is required, including for
    /// undelivered invoices. Text is HTML-encoded and URL attributes are prepared separately. Formatting
    /// failures propagate.
    /// </remarks>
    /// <exception cref="Niobium.ApplicationException">A token is missing or does not match, or the embedded template is missing.</exception>
    public async Task<string> BuildHTMLAsync(string token, CancellationToken cancellationToken)
    {
        Invoice entity = await GetEntityAsync(cancellationToken);
        if (!entity.MatchesAccessToken(token))
        {
            throw new ApplicationException(InternalError.Forbidden, "Invalid token.") { Reference = entity.GetFullID() };
        }

        TimeZoneInfo timezone = TimeZoneInfoHelper.ParseTimeZoneFromIANA(entity.TimeZone);
        CultureInfo culture = CultureInfo.GetCultureInfo(entity.Culture, true);
        htmlTemplate ??= await R.GetEmbededResourceAsStringAsync(Constants.InvoiceTemplateResourceName, cancellationToken)
            ?? throw new ApplicationException(InternalError.InternalServerError, "Missing invoice template.");
        Dictionary<string, string> parameters = entity.BuildTemplateParameters(timezone, culture).ToDictionary();
        parameters["BILLER_LOGO"] = HtmlTemplate.ImageUrl(entity.BillerLogo);
        parameters["CONTACT_EMAIL_HREF"] = HtmlTemplate.ContactUrl("mailto", entity.ContactEmailAddress);
        parameters["CONTACT_PHONE_HREF"] = HtmlTemplate.ContactUrl("tel", entity.ContactPhoneNumber);
        return HtmlTemplate.Render(htmlTemplate, parameters);
    }

    /// <summary>
    /// Reuses the saved link token, or creates and saves one before a notification can be queued.
    /// </summary>
    /// <param name="salt">Full secret used only when a token has not previously been saved.</param>
    /// <param name="cancellationToken">Token forwarded to loading and persistence.</param>
    /// <returns>The existing token unchanged, or the newly persisted v2 token.</returns>
    /// <remarks>
    /// Existing emailed links remain usable across resends and secret changes. No delivery timestamp is
    /// set here. Persistence failures propagate before enqueueing; this does not make the wider invoice
    /// workflow atomic or provide a new concurrency policy.
    /// </remarks>
    public async Task<string> EnsureAccessTokenAsync(string salt, CancellationToken cancellationToken)
    {
        Invoice entity = await GetEntityAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(entity.Token))
        {
            return entity.Token;
        }

        string? previousToken = entity.Token;
        string token = entity.BuildAccessToken(salt);
        entity.Token = token;
        try
        {
            await SaveAsync(cancellationToken: cancellationToken);
        }
        catch
        {
            entity.Token = previousToken;
            throw;
        }
        return token;
    }

    /// <summary>
    /// Builds formatted invoice values and a public invoice URL for notification templates.
    /// </summary>
    /// <param name="token">Link token inserted into the notification URL.</param>
    /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
    /// <returns>Invoice display values including INVOICE_URL.</returns>
    /// <remarks>
    /// Formatting and configuration failures propagate; this method does not persist or send.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string>> BuildNotificationParametersAsync(string token, CancellationToken cancellationToken)
    {
        Invoice entity = await GetEntityAsync(cancellationToken);
        TimeZoneInfo timezone = TimeZoneInfoHelper.ParseTimeZoneFromIANA(entity.TimeZone);
        CultureInfo culture = CultureInfo.GetCultureInfo(entity.Culture, true);
        var parameters = entity.BuildTemplateParameters(timezone, culture).ToDictionary();
        parameters.Add("INVOICE_URL", $"{config.Value.GetInvoiceEndpoint}/{entity.Biller}/invoices/{entity.GetID()}?token={Uri.EscapeDataString(token)}");
        return parameters;
    }

    /// <summary>
    /// Stores the link token and current UTC enqueue-related timestamp, then saves the invoice.
    /// </summary>
    /// <param name="token">Link token to persist on the invoice.</param>
    /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
    /// <returns>A task completing after persistence.</returns>
    /// <remarks>
    /// Called after enqueueing; this is not recipient-delivery confirmation. Persistence failure can occur
    /// after the notification has already been accepted by the broker.
    /// </remarks>
    public async Task OnDeliveredAsync(string token, CancellationToken cancellationToken)
    {
        Invoice entity = await GetEntityAsync(cancellationToken);
        entity.Token = token;
        entity.Delivered = DateTimeOffset.UtcNow;
        await SaveAsync(cancellationToken: cancellationToken);
    }
}
