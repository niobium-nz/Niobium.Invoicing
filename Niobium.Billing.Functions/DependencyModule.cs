using Cod;
using Cod.Platform;
using Cod.Platform.StorageTable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Niobium.Billing.Functions
{
    public static class DependencyModule
    {
        private static volatile bool loaded;

        public static IServiceCollection AddBilling(this IServiceCollection services, IConfiguration configuration)
        {
            return services.AddBilling(configuration.Bind);
        }

        public static IServiceCollection AddBilling(this IServiceCollection services, Action<BillingOptions>? options = null)
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
            return services.RegisterDomain<InvoiceDomain, Invoice>()
                .AddTransient<IResourceControl, OwnershipControl<InvoiceItem, Invoice>>();
        }
    }
}
