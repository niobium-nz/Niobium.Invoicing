using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Logging;
using Niobium.Invoicing.Options;
using Niobium.Messaging;

namespace Niobium.Invoicing
{
    public static class DependencyModule
    {
        private static volatile bool loaded;

        public static TBuilder AddCore<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            Database.StorageTable.DependencyModule.AddDatabase(builder);
            Profile.DependencyModule.AddProfile(builder);
            builder.AddCore(builder.Configuration.GetSection(nameof(BillingOptions)).Bind);
            return builder;
        }

        private static TBuilder AddCore<TBuilder>(this TBuilder builder, Action<BillingOptions>? options) where TBuilder : IHostApplicationBuilder
        {
            if (loaded)
            {
                return builder;
            }

            loaded = true;

            bool isDevelopment = builder.Configuration.IsDevelopmentEnvironment();
            builder.Services.AddMessaging(isDevelopment);
            IdentityModelEventSource.ShowPII = isDevelopment;

            builder.Services.Configure<BillingOptions>(o =>
            {
                options?.Invoke(o);
            });

            builder.Services.RegisterDomainComponents(typeof(DependencyModule));
            return builder;
        }
    }
}
