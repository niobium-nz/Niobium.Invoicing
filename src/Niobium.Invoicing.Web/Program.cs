using Niobium.Invoicing.Web;
WebApplication.CreateBuilder(args)
    .AddInvoicing()
    .Build()
    .UseInvoicing()
    .Run();