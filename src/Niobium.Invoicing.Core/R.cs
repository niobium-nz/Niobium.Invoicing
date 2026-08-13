using System.Reflection;

namespace Niobium.Invoicing
{
    internal class R
    {
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
