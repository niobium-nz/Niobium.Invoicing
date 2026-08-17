using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using Niobium.Invoicing;
using Niobium.Platform.Functions;
using Niobium.Platform.Identity;

FunctionsApplicationBuilder builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.AddCore();
builder.ToMiddlewareHost().UsePlatformIdentity();
builder.Build().Run();
