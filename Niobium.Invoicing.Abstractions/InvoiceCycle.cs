using System.ComponentModel.DataAnnotations;

namespace Niobium.Invoicing
{
    public enum InvoiceCycle : int
    {
        [Display(Name = "Once")]
        Once = 0,

        [Display(Name = "Daily")]
        Daily = 1,

        [Display(Name = "Monthly")]
        Monthly = 2,

        [Display(Name = "Anually")]
        Anually = 3,

        [Display(Name = "Custom Range")]
        Range = 4
    }
}
