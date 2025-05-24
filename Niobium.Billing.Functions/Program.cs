using Cod;
using Cod.Database.StorageTable;
using Cod.Platform;
using Cod.Platform.Identity;
using Cod.Platform.Notification.Email.Resend;
using Cod.Platform.StorageTable;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Logging;
using Niobium.Billing;
using Niobium.Billing.Functions;
using System.Text;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Host.CreateDefaultBuilder(args)
    .ConfigureFunctionsWebApplication(builder =>
    {
        builder.UsePlatformIdentity();
    })
    .ConfigureServices((context, services) =>
    {
        var isDevelopment = context.Configuration.IsDevelopmentEnvironment();
        IdentityModelEventSource.ShowPII = isDevelopment;
        var identityOptions = context.Configuration.GetRequiredSection(nameof(IdentityServiceOptions));
        services.AddApplicationInsightsTelemetryWorkerService()
                .ConfigureFunctionsApplicationInsights()
                .AddDatabase(context.Configuration.GetRequiredSection(nameof(StorageTableOptions)))
                    .PostConfigure<StorageTableOptions>(opt => opt.EnableInteractiveIdentity = isDevelopment)
                    .AddDatabaseResourceTokenSupport(identityOptions)
                    .GrantDatabasePersonalizedEntitlementTo(nameof(Invoice), 
                        DatabasePermissions.Query | DatabasePermissions.Add)
                    .GrantDatabaseEntitlementTo(nameof(InvoiceItem),
                        DatabasePermissions.Query | DatabasePermissions.Add)
                    .GrantDatabasePersonalizedEntitlementTo(nameof(Billable),
                        DatabasePermissions.Query | DatabasePermissions.Add | DatabasePermissions.Delete)
                    .GrantDatabasePersonalizedEntitlementTo(nameof(Billee), 
                    DatabasePermissions.Query | DatabasePermissions.Add | DatabasePermissions.Delete | DatabasePermissions.Update)
                .AddIdentity(identityOptions)
                .AddBilling(context.Configuration.GetRequiredSection(nameof(BillingOptions)))
                .AddNotification(context.Configuration.GetRequiredSection(nameof(ResendServiceOptions)));
    })
    .UseDefaultServiceProvider((_, options) =>
    {
        options.ValidateScopes = true;
        options.ValidateOnBuild = true;
    })
    .Build()
    .Run();