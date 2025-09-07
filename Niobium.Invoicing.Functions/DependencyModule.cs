using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Logging;
using Niobium.Database.StorageTable;
using Niobium.Invoicing.Options;
using Niobium.Messaging.ServiceBus;
using Niobium.Notification;
using Niobium.Platform;
using Niobium.Platform.Identity;
using Niobium.Platform.Profile;
using Niobium.Platform.StorageTable;

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

            bool isDevelopment = builder.Configuration.IsDevelopmentEnvironment();
            IdentityModelEventSource.ShowPII = isDevelopment;

            builder.UsePlatformIdentity();

            builder.AddIdentity();
            builder.AddProfile(useServicePrincipalAuthentication: true);
            builder.AddDatabase();
            builder.AddDatabaseResourceTokenSupport();
            builder.Services.AddResourceControl<OwnershipControl<InvoiceItem, Invoice>>();
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Invoice));
            builder.Services.GrantDatabaseEntitlementTo(nameof(InvoiceItem));
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Billable),
                DatabasePermissions.Query | DatabasePermissions.Add | DatabasePermissions.Delete);
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Billee),
                DatabasePermissions.Query | DatabasePermissions.Add | DatabasePermissions.Delete | DatabasePermissions.Update);
            builder.Services.AddMessagingBroker<NotifyCommand>(isDevelopment, builder.Configuration.GetSection(nameof(NotificationQueueOptions)).Bind);

            builder.Services.AddCore(builder.Configuration.GetRequiredSection(nameof(BillingOptions)).Bind);

        }
    }
}
