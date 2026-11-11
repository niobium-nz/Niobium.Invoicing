using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Niobium.Invoicing.Options;
using Niobium.Messaging.ServiceBus;
using Niobium.Notification;
using Niobium.Platform;
using Niobium.Platform.Identity;
using Niobium.Platform.Profile;
using Niobium.Platform.ServiceBus;
using Niobium.Platform.StorageTable;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Niobium.Invoicing.Server
{
    /// <summary>
    /// Composes Functions infrastructure, identity, table entitlements and notification transport.
    /// </summary>
    public static class DependencyModule
    {
        /// <summary>
        /// Process-wide guard preventing repeated registration by this module.
        /// </summary>
        private static volatile bool loaded;

        /// <summary>
        /// Registers Functions, identity, profile, messaging, table support and Core services.
        /// </summary>
        /// <typeparam name="TBuilder">Host builder type supporting the required registrations.</typeparam>
        /// <param name="builder">Host builder receiving the service registrations.</param>
        /// <returns>The supplied host builder.</returns>
        public static TBuilder AddInvoicing<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            Platform.Functions.DependencyModule.AddPlatform(builder);
            builder.AddIdentity();
            builder.AddProfile(useServicePrincipalAuthentication: true);
            builder.AddMessaging();
            builder.AddDatabase();
            builder.AddDatabaseResourceTokenSupport();
            builder.AddCore();
            builder.Services.AddInvoiceQueueTelemetry(builder.Configuration);
            builder.AddInvoicing(builder.Configuration.GetSection(nameof(BillingOptions)).Bind);
            return builder;
        }

        /// <summary>
        /// Adds application measurements to the platform's existing telemetry pipeline.
        /// Preserves explicit service identity after the Functions resource detector runs.
        /// Exporters remain owned by the installed Niobium platform.
        /// </summary>
        public static IServiceCollection AddInvoiceQueueTelemetry(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddMetrics();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<InvoiceQueueTelemetry>();
            services.AddOpenTelemetry().ConfigureResource(resource =>
            {
                // Functions 1.2.0 adds its detector after Platform's resource attributes. Keep
                // explicitly configured service identity while retaining the detector's metadata.
                List<KeyValuePair<string, object>> attributes = [];
                string? name = configuration["SERVICE_NAME"];
                string? version = configuration["SERVICE_VERSION"];
                if (!string.IsNullOrWhiteSpace(name))
                    attributes.Add(new("service.name", name));
                if (!string.IsNullOrWhiteSpace(version))
                    attributes.Add(new("service.version", version));
                resource.AddAttributes(attributes);
            }).WithMetrics(metrics => metrics.AddMeter(InvoiceQueueTelemetry.MeterName));
            return services;
        }

        /// <summary>
        /// Registers resource ownership controls, default table grants and the notification broker once per
        /// process.
        /// </summary>
        /// <typeparam name="TBuilder">Host builder type supporting the required registrations.</typeparam>
        /// <param name="builder">Host builder receiving the service registrations.</param>
        /// <param name="options">Accepted binding callback; not invoked by this overload.</param>
        /// <returns>The supplied builder.</returns>
        /// <remarks>
        /// The static guard is shared across builders. Billing options are configured by Core; this overload
        /// does not invoke its options callback.
        /// </remarks>
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
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Billable),
                DatabasePermissions.Query | DatabasePermissions.Add | DatabasePermissions.Delete);
            builder.Services.GrantDatabasePersonalizedEntitlementTo(nameof(Billee),
                DatabasePermissions.Query | DatabasePermissions.Add | DatabasePermissions.Delete | DatabasePermissions.Update);
            builder.Services.AddMessagingBroker<NotifyCommand>(isDevelopment, builder.Configuration.GetSection(nameof(NotificationQueueOptions)).Bind);

            return builder;
        }
    }
}
