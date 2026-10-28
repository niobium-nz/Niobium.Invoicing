using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Niobium.Invoicing.Server
{
    /// <summary>
    /// Declares Functions routes intercepted by Niobium access-token and resource-token middleware.
    /// </summary>
    public class Placeholder
    {
        /// <summary>
        /// Exposes the default access-token route for interception by Niobium middleware.
        /// </summary>
        /// <param name="req">Incoming Functions HTTP request.</param>
        /// <returns>An empty 200 fallback if middleware does not handle the request.</returns>
        /// <remarks>
        /// Token issuance belongs to AccessTokenMiddleware, not this method.
        /// </remarks>
        [Function(nameof(Auth))]
        public IActionResult Auth([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = Niobium.Identity.Constants.DefaultAccessTokenEndpoint)] HttpRequest req)
        {
            return new OkResult();
        }

        /// <summary>
        /// Exposes the default resource-token route for interception by Niobium middleware.
        /// </summary>
        /// <param name="req">Incoming Functions HTTP request.</param>
        /// <returns>An empty 200 fallback if middleware does not handle the request.</returns>
        /// <remarks>
        /// SAS issuance and authorization belong to ResourceTokenMiddleware and resource controls.
        /// </remarks>
        [Function(nameof(RSAS))]
        public IActionResult RSAS([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = Niobium.Identity.Constants.DefaultResourceTokenEndpoint)] HttpRequest req)
        {
            return new OkResult();
        }
    }
}
