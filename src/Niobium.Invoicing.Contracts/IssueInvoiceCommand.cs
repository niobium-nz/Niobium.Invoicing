using System.Text.Json.Serialization;

namespace Niobium.Invoicing;

/// <summary>
/// Trusted integration request with an embedded customer and domain-event metadata.
/// </summary>
public class IssueInvoiceCommand : IssueInvoiceRequest, IDomainEvent, IUserInput
{
    /// <summary>
    /// Initializes both domain-event audiences to Internal.
    /// </summary>
    public IssueInvoiceCommand()
    {
        Source = DomainEventAudience.Internal;
        Target = DomainEventAudience.Internal;
    }

    /// <summary>
    /// Domain-event identifier, default a new GUID string; separate from InvoiceID and not used as an
    /// invoice replay guard.
    /// </summary>
    public string ID { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Domain-event occurrence time, excluded from JSON; retains default unless assigned.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset Occurried
    {
        get; set;
    }

    /// <summary>
    /// Internal source audience set by the constructor and excluded from JSON.
    /// </summary>
    [JsonIgnore]
    public DomainEventAudience Source
    {
        get; set;
    }

    /// <summary>
    /// Internal target audience set by the constructor and excluded from JSON.
    /// </summary>
    [JsonIgnore]
    public DomainEventAudience Target
    {
        get; set;
    }

    /// <summary>
    /// Embedded customer snapshot used by trusted queue issuance; Sanitize fills a missing BilleeID.
    /// </summary>
    public required Billee Billee
    {
        get; set;
    }

    /// <summary>
    /// Fills a missing BilleeID from the embedded customer without hiding conflicting identifiers.
    /// </summary>
    /// <remarks>
    /// This normalization does not validate customer ownership or invoice item relationships.
    /// </remarks>
    public void Sanitize()
    {
        if (BilleeID == Guid.Empty && Billee != null)
        {
            BilleeID = Billee.ID;
        }
    }
}
