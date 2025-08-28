using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Niobium.Invoicing.Functions
{
    public class Placeholder
    {
        [Function(nameof(Auth))]
        public IActionResult Auth([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = Niobium.Identity.Constants.DefaultAccessTokenEndpoint)] HttpRequest req)
        {
            return new OkResult();
        }

        [Function(nameof(RSAS))]
        public IActionResult RSAS([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = Niobium.Identity.Constants.DefaultResourceTokenEndpoint)] HttpRequest req)
        {
            return new OkResult();
        }
    }
}
