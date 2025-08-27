using Niobium;
using Niobium.Database.StorageTable;
using Niobium.Platform;
using Niobium.Platform.Identity;
using Niobium.Platform.Notification.Email.Resend;
using Niobium.Platform.Profile;
using Niobium.Platform.StorageTable;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Logging;

namespace Niobium.Invoicing.Functions
{
    internal static class DependencyModule
    {
        private static volatile bool loaded;

        public static void AddInvoicing(this FunctionsApplicationBuilder builder)
        {
            if (loaded)
            {
                return;
            }

            loaded = true;

            var isDevelopment = builder.Configuration.IsDevelopmentEnvironment();
            IdentityModelEventSource.ShowPII = isDevelopment;

            builder.UsePlatformIdentity();

            builder.AddIdentity();
            builder.AddProfile();
            builder.AddNotification();
            builder.AddDatabase();
            builder.AddDatabaseResourceTokenSupport();
            builder.Services.AddResourceControl<OwnershipControl<InvoiceItem, Invoice>>();
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Invoice), DatabasePermissions.Query);
            builder.Services.GrantDatabaseEntitlementTo(nameof(InvoiceItem), DatabasePermissions.Query);
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Billable),
                DatabasePermissions.Query | DatabasePermissions.Add | DatabasePermissions.Delete);
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Billee),
                DatabasePermissions.Query | DatabasePermissions.Add | DatabasePermissions.Delete | DatabasePermissions.Update);

            builder.Services.AddInvoicing(builder.Configuration.GetRequiredSection(nameof(BillingOptions)).Bind);
        }
    }
}
