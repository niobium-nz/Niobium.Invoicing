using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Niobium.Database.StorageTable;
using Niobium.Invoicing.Functions;
using Niobium.Platform.Identity;
using Niobium.Platform.StorageTable;

namespace Niobium.Invoicing.Functions
{
    internal static class IServiceCollectionExtensions
    {
        public static IServiceCollection GrantDatabasePersonalizedEntitlementTo(this IServiceCollection services, string table, DatabasePermissions? permissions = null)
        {
            permissions ??= DatabasePermissions.Query;
            services.GrantDatabasePersonalizedEntitlementTo(
                        sp => sp.GetRequiredService<IOptions<IdentityServiceOptions>>().Value.DefaultRole,
                        permissions.Value,
                        sp => table,
                        sp => sp.GetRequiredService<IOptions<StorageTableOptions>>().Value.FullyQualifiedDomainName!);

            return services;
        }

        public static IServiceCollection GrantDatabaseEntitlementTo(this IServiceCollection services, string table, DatabasePermissions? permissions = null)
        {
            permissions ??= DatabasePermissions.Query;
            services.GrantDatabaseEntitlementTo(
                        sp => sp.GetRequiredService<IOptions<IdentityServiceOptions>>().Value.DefaultRole,
                        permissions.Value,
                        sp => table,
                        sp => sp.GetRequiredService<IOptions<StorageTableOptions>>().Value.FullyQualifiedDomainName!);

            return services;
        }
    }
}
