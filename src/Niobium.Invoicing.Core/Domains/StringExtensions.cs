using System.Text;

namespace Niobium.Invoicing.Domains;

/// <summary>
/// Converts model member names into the uppercase template-key convention.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Converts member-style names to uppercase underscore-separated template keys.
    /// </summary>
    /// <param name="text">Non-null member-style text to convert.</param>
    /// <returns>The uppercase key, with ID normalized before splitting capital letters.</returns>
    /// <remarks>
    /// This is the application naming convention, not a general word/acronym tokenizer.
    /// </remarks>
    public static string ToSnakeCaseUpper(this string text)
    {
        if (text.Length < 2)
        {
            return text.ToUpperInvariant();
        }

        text = text.Replace("ID", "Id", StringComparison.InvariantCulture);

        StringBuilder sb = new();
        sb.Append(char.ToUpperInvariant(text[0]));
        for (int i = 1; i < text.Length; ++i)
        {
            char c = text[i];
            if (char.IsUpper(c))
            {
                sb.Append('_');
                sb.Append(char.ToUpperInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString().ToUpperInvariant();
    }
}
