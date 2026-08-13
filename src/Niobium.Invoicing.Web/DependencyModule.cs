using Microsoft.IdentityModel.Logging;
using Niobium.Messaging.ServiceBus;
using Niobium.Notification;
using Niobium.Platform;
using Niobium.Platform.Identity;
using Niobium.Platform.Profile;
using Niobium.Platform.ServiceBus;
using Niobium.Platform.StorageTable;

namespace Niobium.Invoicing.Web
{
    internal static class DependencyModule
    {
        private static volatile bool loaded;

        public static WebApplicationBuilder AddInvoicing(this WebApplicationBuilder builder)
        {
            if (loaded)
            {
                return builder;
            }

            loaded = true;

            bool isDevelopment = builder.Configuration.IsDevelopmentEnvironment();
            IdentityModelEventSource.ShowPII = isDevelopment;

            builder.AddIdentity();
            builder.AddProfile(useServicePrincipalAuthentication: true);
            builder.AddMessaging();
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

            builder.AddCore();
            return builder;
        }

        public static WebApplication UseInvoicing(this WebApplication app)
        {
            app.UseDapr();
            app.UsePlatformIdentity();
            return app;
        }
    }
}
