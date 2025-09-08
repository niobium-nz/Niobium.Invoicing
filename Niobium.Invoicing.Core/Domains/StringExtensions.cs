using System.Text;

namespace Niobium.Invoicing.Domains
{
    internal static class StringExtensions
    {
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
}
