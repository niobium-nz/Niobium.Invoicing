using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Niobium.Invoicing.Options;

namespace Niobium.Invoicing
{
    public static class DependencyModule
    {
        private static volatile bool loaded;

        public static void AddCore(this IHostApplicationBuilder builder)
            => builder.Services.AddCore(builder.Configuration.GetSection(nameof(BillingOptions)).Bind);

        private static IServiceCollection AddCore(this IServiceCollection services, Action<BillingOptions>? options = null)
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
            return services.RegisterDomainComponents(typeof(DependencyModule));
        }
    }
}
