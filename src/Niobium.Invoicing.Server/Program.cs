using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using Niobium.Invoicing.Server;
using Niobium.Platform.Functions;
using Niobium.Platform.Identity;

FunctionsApplicationBuilder builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.AddInvoicing();
builder.ToMiddlewareHost().UsePlatformIdentity();
builder.Build().Run();
