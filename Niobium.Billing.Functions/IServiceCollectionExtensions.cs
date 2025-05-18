using Cod.Database.StorageTable;
using Cod.Platform.Identity;
using Cod.Platform.StorageTable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Niobium.Billing.Functions
{
    internal static class IServiceCollectionExtensions
    {
        public static IServiceCollection GrantDatabasePersonalizedEntitlementTo(this IServiceCollection services, string table)
        {
            services.GrantDatabasePersonalizedEntitlementTo(
                        sp => sp.GetRequiredService<IOptions<IdentityServiceOptions>>().Value.DefaultRole,
                        Cod.DatabasePermissions.Query,
                        sp => table,
                        sp => sp.GetRequiredService<IOptions<StorageTableOptions>>().Value.FullyQualifiedDomainName!);

            return services;
        }
    }
}
