using Niobium;

namespace Niobium.Invoicing
{
    public class IssueInvoiceCommand : IssueInvoiceRequest, IUserInput
    {
        public required Billee Billee { get; set; }

        public void Sanitize()
        {
            BilleeID = Billee.ID;
        }
    }
}
