using Microsoft.Extensions.DependencyInjection;
using Niobium.Invoicing.Functions;

namespace Niobium.Invoicing
{
    public static class DependencyModule
    {
        private static volatile bool loaded;

        public static IServiceCollection AddInvoicing(this IServiceCollection services, Action<BillingOptions>? options = null)
        {
            if (loaded)
            {
                return services;
            }

            loaded = true;

            services.Configure<BillingOptions>(o =>
            {
                options?.Invoke(o);
            });
            return services.AddDomain<InvoiceDomain, Invoice>();
        }
    }
}
