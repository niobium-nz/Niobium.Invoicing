using System.Globalization;
using Microsoft.Extensions.Options;
using Niobium.Invoicing.Options;

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
        parameters.Add("INVOICE_URL", $"{entity.IssuanceNotificationEndpoint ?? config.Value.GetInvoiceEndpoint}/{entity.Biller}/invoices/{entity.GetID()}?token={Uri.EscapeDataString(token)}");
        return parameters;
    }

    /// <summary>
    /// Records the first enqueue completion time for the matching frozen invoice.
    /// </summary>
    /// <param name="token">Link token already persisted before enqueue.</param>
    /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
    /// <returns>A task completing after persistence.</returns>
    /// <remarks>
    /// Called after enqueueing; this is not recipient-delivery confirmation. Persistence failure can occur
    /// after the notification has already been accepted by the broker.
    /// </remarks>
    public async Task OnDeliveredAsync(string token, CancellationToken cancellationToken)
    {
        Invoice entity = await GetEntityAsync(cancellationToken);
        if (entity.IssuanceState != InvoiceContent.Frozen || entity.Token != token)
        {
            throw Conflict("Notification does not match the frozen invoice.");
        }
        if (entity.Delivered.HasValue)
        {
            return;
        }
        Invoice next = InvoiceContent.Copy(entity);
        next.Delivered = DateTimeOffset.UtcNow;
        await PersistInvoiceAsync(next, cancellationToken);
    }
}
