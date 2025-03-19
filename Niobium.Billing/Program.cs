using Cod.Database.StorageTable;
using Cod.Platform;
using Cod.Platform.StorageTable;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Niobium.Billing;
using System.Text;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Host.CreateDefaultBuilder(args)
    .ConfigureFunctionsWebApplication(builder =>
    {
        builder.UsePlatform();
    })
    .ConfigureServices((context, services) =>
    {
        var isDevelopment = context.Configuration.IsDevelopmentEnvironment();
        services.AddApplicationInsightsTelemetryWorkerService()
                .ConfigureFunctionsApplicationInsights()
                .AddDatabase(context.Configuration.GetRequiredSection(nameof(StorageTableOptions)))
                    .PostConfigure<StorageTableOptions>(opt => opt.EnableInteractiveIdentity = isDevelopment)
                .AddBilling(context.Configuration.GetRequiredSection(nameof(BillingOptions)));
    })
    .UseDefaultServiceProvider((_, options) =>
    {
        options.ValidateScopes = true;
        options.ValidateOnBuild = true;
    })
    .Build()
    .Run();