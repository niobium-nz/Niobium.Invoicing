using System.Text.Json.Serialization;

namespace Niobium.Invoicing
{
    public class IssueInvoiceCommand : IssueInvoiceRequest, IDomainEvent, IUserInput
    {
        public IssueInvoiceCommand()
        {
            Source = DomainEventAudience.Internal;
            Target = DomainEventAudience.Internal;
        }

        public string ID { get; set; } = Guid.NewGuid().ToString();

        [JsonIgnore]
        public DateTimeOffset Occurried { get; set; }

        [JsonIgnore]
        public DomainEventAudience Source { get; set; }

        [JsonIgnore]
        public DomainEventAudience Target { get; set; }

        public required Billee Billee { get; set; }

        public void Sanitize()
        {
            BilleeID = Billee.ID;
        }
    }
}
