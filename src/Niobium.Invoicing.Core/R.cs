using System.Reflection;

namespace Niobium.Invoicing
{
    /// <summary>
    /// Reads embedded text resources from the Core assembly.
    /// </summary>
    internal class R
    {
        /// <summary>
        /// Reads a named embedded Core resource as text.
        /// </summary>
        /// <param name="resourceName">Exact manifest resource name.</param>
        /// <param name="cancellationToken">Token observed by the asynchronous stream read.</param>
        /// <returns>The resource contents, or null when the resource is absent.</returns>
        public static async Task<string?> GetEmbededResourceAsStringAsync(string resourceName, CancellationToken cancellationToken = default)
        {
            Assembly assembly = typeof(R).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return null;
            }

            using StreamReader reader = new(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
    }
}
