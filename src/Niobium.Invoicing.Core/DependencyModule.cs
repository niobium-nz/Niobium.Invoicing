using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Logging;
using Niobium.Invoicing.Options;
using Niobium.Messaging;

namespace Niobium.Invoicing;

/// <summary>
/// Registers invoice domains, flows, repositories, profile access and billing options.
/// </summary>
public static class DependencyModule
{
    /// <summary>
    /// Process-wide guard preventing repeated registration by this module.
    /// </summary>
    private static volatile bool loaded;

    /// <summary>
    /// Registers table/profile infrastructure and binds the BillingOptions section.
    /// </summary>
    /// <typeparam name="TBuilder">Host builder type supporting the required registrations.</typeparam>
    /// <param name="builder">Host builder receiving the service registrations.</param>
    /// <returns>The supplied builder for further configuration.</returns>
    public static TBuilder AddCore<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        Database.StorageTable.DependencyModule.AddDatabase(builder);
        Profile.DependencyModule.AddProfile(builder);
        builder.AddCore(builder.Configuration.GetSection(nameof(BillingOptions)).Bind);
        return builder;
    }

    /// <summary>
    /// Registers Core components once per process and selects development messaging behavior.
    /// </summary>
    /// <typeparam name="TBuilder">Host builder type supporting the required registrations.</typeparam>
    /// <param name="builder">Host builder receiving the service registrations.</param>
    /// <param name="options">Optional callback binding billing configuration.</param>
    /// <returns>The supplied builder, unchanged on subsequent registration calls.</returns>
    /// <remarks>
    /// The static guard is shared by host builders; development also enables IdentityModel PII logging.
    /// </remarks>
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
