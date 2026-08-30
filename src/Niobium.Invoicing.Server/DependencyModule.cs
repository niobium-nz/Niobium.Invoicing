using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Niobium.Invoicing.Options;
using Niobium.Messaging.ServiceBus;
using Niobium.Notification;
using Niobium.Platform;
using Niobium.Platform.Identity;
using Niobium.Platform.Profile;
using Niobium.Platform.ServiceBus;
using Niobium.Platform.StorageTable;

namespace Niobium.Invoicing.Server
{
    public static class DependencyModule
    {
        private static volatile bool loaded;

        public static TBuilder AddInvoicing<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            Platform.Functions.DependencyModule.AddPlatform(builder);
            builder.AddIdentity();
            builder.AddProfile(useServicePrincipalAuthentication: true);
            builder.AddMessaging();
            builder.AddDatabase();
            builder.AddDatabaseResourceTokenSupport();
            builder.AddCore();
            builder.AddInvoicing(builder.Configuration.GetSection(nameof(BillingOptions)).Bind);
            return builder;
        }

        private static TBuilder AddInvoicing<TBuilder>(this TBuilder builder, Action<BillingOptions>? options) where TBuilder : IHostApplicationBuilder
        {
            if (loaded)
            {
                return builder;
            }

            loaded = true;

            bool isDevelopment = builder.Configuration.IsDevelopmentEnvironment();
            builder.Services.AddResourceControl<OwnershipControl<InvoiceItem, Invoice>>();
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Invoice));
            builder.Services.GrantDatabaseEntitlementTo(nameof(InvoiceItem));
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Billable),
                DatabasePermissions.Query | DatabasePermissions.Add | DatabasePermissions.Delete);
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Billee),
                DatabasePermissions.Query | DatabasePermissions.Add | DatabasePermissions.Delete | DatabasePermissions.Update);
            builder.Services.AddMessagingBroker<NotifyCommand>(isDevelopment, builder.Configuration.GetSection(nameof(NotificationQueueOptions)).Bind);

            return builder;
        }
    }
}
