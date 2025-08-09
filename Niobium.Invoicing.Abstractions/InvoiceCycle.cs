using System.ComponentModel.DataAnnotations;

namespace Niobium.Invoicing
{
    public enum InvoiceCycle : int
    {
        [Display(Name = "Daily")]
        Daily = 0,

        [Display(Name = "Monthly")]
        Monthly = 1,

        [Display(Name = "Anually")]
        Anually = 2,

        [Display(Name = "Custom Range")]
        Range = 3
    }
}
