using Cod;
using Microsoft.Extensions.DependencyInjection;

namespace Niobium.Billing
{
    public static class DependencyModule
    {
        public static IServiceCollection AddBilling(this IServiceCollection services)
        {
            return services.RegisterDomain<InvoiceDomain, Invoice>();
        }
    }
}
