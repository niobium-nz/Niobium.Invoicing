namespace Niobium.Invoicing;

/// <summary>
/// Names used by invoice rendering and notification dispatch.
/// </summary>
internal class Constants
{
    /// <summary>
    /// Manifest name of the embedded invoice HTML resource.
    /// </summary>
    public const string InvoiceTemplateResourceName = "Niobium.Invoicing.InvoiceTemplate.html";
    /// <summary>
    /// Email-template resource name; no matching resource is embedded or used by current flows.
    /// </summary>
    public const string EmailTemplateResourceName = "Niobium.Invoicing.EmailTemplate.html";

    public const string InvoiceNotificationChannel = "Invoice";
}
