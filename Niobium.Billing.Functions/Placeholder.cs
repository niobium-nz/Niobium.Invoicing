using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Niobium.Billing.Functions
{
    public class Placeholder
    {
        [Function(nameof(Auth))]
        public IActionResult Auth([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = Cod.Identity.Constants.DefaultAccessTokenEndpoint)] HttpRequest req) => new OkResult();

        [Function(nameof(RSAS))]
        public IActionResult RSAS([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = Cod.Identity.Constants.DefaultResourceTokenEndpoint)] HttpRequest req) => new OkResult();
    }
}
