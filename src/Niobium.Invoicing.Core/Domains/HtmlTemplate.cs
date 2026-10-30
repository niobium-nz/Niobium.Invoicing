using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Niobium.Invoicing.Domains
{
    /// <summary>
    /// Renders the embedded templates with encoded values and explicitly prepared URL attributes.
    /// </summary>
    internal static partial class HtmlTemplate
    {
        /// <summary>
        /// Replaces known placeholders once, encoding values for HTML text or quoted attributes.
        /// </summary>
        /// <param name="template">Trusted embedded markup containing uppercase placeholders.</param>
        /// <param name="parameters">Unencoded display values; URL values must already use an allowed scheme.</param>
        /// <returns>Markup with encoded substitutions and unknown placeholders preserved.</returns>
        /// <remarks>Inserted values are never interpreted as further template placeholders.</remarks>
        public static string Render(string template, IReadOnlyDictionary<string, string> parameters)
        {
            return PlaceholderRegex().Replace(template, match =>
                parameters.TryGetValue(match.Groups[1].Value, out string? value)
                    ? HtmlEncoder.Default.Encode(value)
                    : match.Value);
        }

        /// <summary>
        /// Accepts an absolute HTTP or HTTPS image URL and rejects other schemes and control characters.
        /// </summary>
        /// <param name="value">Optional logo URL supplied by a biller.</param>
        /// <returns>The normalized URL, or an empty string when absent or disallowed.</returns>
        public static string ImageUrl(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !value.Any(char.IsControl)
                && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                    ? uri.AbsoluteUri
                    : string.Empty;
        }

        /// <summary>
        /// Encodes a contact value as URI data beneath a trusted, fixed scheme.
        /// </summary>
        /// <param name="scheme">Application-selected scheme, either mailto or tel.</param>
        /// <param name="value">Optional email address or phone number.</param>
        /// <returns>A URI without user-supplied query syntax, or an empty string for a missing value.</returns>
        public static string ContactUrl(string scheme, string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : $"{scheme}:{Uri.EscapeDataString(value.Trim())}";
        }

        /// <summary>
        /// Matches the placeholder syntax used by the embedded invoice and line templates.
        /// </summary>
        /// <returns>The generated placeholder expression.</returns>
        [GeneratedRegex(@"\{\{([A-Z0-9_]+)\}\}", RegexOptions.CultureInvariant)]
        private static partial Regex PlaceholderRegex();
    }
}