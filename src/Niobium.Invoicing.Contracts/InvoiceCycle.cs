using System.ComponentModel.DataAnnotations;

namespace Niobium.Invoicing
{
    /// <summary>
    /// Selects billing-period presentation; these values do not schedule recurring issuance.
    /// </summary>
    public enum InvoiceCycle : int
    {
        /// <summary>
        /// Omits the billing-period label.
        /// </summary>
        [Display(Name = "Once")]
        Once = 0,

        /// <summary>
        /// Displays the localized billing-period start day.
        /// </summary>
        [Display(Name = "Daily")]
        Daily = 1,

        /// <summary>
        /// Displays the localized billing-period start month and year.
        /// </summary>
        [Display(Name = "Monthly")]
        Monthly = 2,

        /// <summary>
        /// Displays the billing-period start year; existing public spelling is retained.
        /// </summary>
        [Display(Name = "Anually")]
        Anually = 3,

        /// <summary>
        /// Displays both localized endpoints when start and end are supplied.
        /// </summary>
        [Display(Name = "Custom Range")]
        Range = 4
    }
}
