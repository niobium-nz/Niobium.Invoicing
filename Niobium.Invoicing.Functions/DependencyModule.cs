using Cod;
using Cod.Database.StorageTable;
using Cod.Platform;
using Cod.Platform.Identity;
using Cod.Platform.Notification.Email.Resend;
using Cod.Platform.StorageTable;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Logging;

namespace Niobium.Invoicing.Functions
{
    public static class DependencyModule
    {
        private static volatile bool loaded;

        public static void AddBilling(this FunctionsApplicationBuilder builder)
        {
            if (loaded)
            {
                return;
            }

            loaded = true;

            var isDevelopment = builder.Configuration.IsDevelopmentEnvironment();
            IdentityModelEventSource.ShowPII = isDevelopment;

            builder.UsePlatformIdentity();

            var identityOptions = builder.Configuration.GetRequiredSection(nameof(IdentityServiceOptions));
            builder.AddDatabase();
            builder.AddDatabaseResourceTokenSupport();
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Invoice),
                DatabasePermissions.Query | DatabasePermissions.Add);
            builder.Services.GrantDatabaseEntitlementTo(nameof(InvoiceItem),
                DatabasePermissions.Query | DatabasePermissions.Add);
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Billable),
                DatabasePermissions.Query | DatabasePermissions.Add | DatabasePermissions.Delete);
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Billee),
                DatabasePermissions.Query | DatabasePermissions.Add | DatabasePermissions.Delete | DatabasePermissions.Update);
            builder.AddIdentity();
            builder.AddNotification();

            builder.Services.AddBilling(builder.Configuration.GetRequiredSection(nameof(BillingOptions)).Bind);
        }

        public static IServiceCollection AddBilling(this IServiceCollection services, Action<BillingOptions>? options = null)
        {
            services.Configure<BillingOptions>(o =>
            {
                options?.Invoke(o);
            });
            return services.AddDomain<InvoiceDomain, Invoice>()
                .AddResourceControl<OwnershipControl<InvoiceItem, Invoice>>();
        }
    }
}
