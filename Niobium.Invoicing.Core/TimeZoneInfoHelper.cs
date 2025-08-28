namespace Niobium.Invoicing
{
    internal class TimeZoneInfoHelper
    {
        public static TimeZoneInfo ParseTimeZoneFromIANA(string ianaTimeZoneID)
        {
            return !TryParseTimeZoneFromIANA(ianaTimeZoneID, out TimeZoneInfo? result)
                ? throw new ArgumentException($"Invalid IANA time zone ID: {ianaTimeZoneID}", nameof(ianaTimeZoneID))
                : result;
        }

        public static bool TryParseTimeZoneFromIANA(string ianaTimeZoneID, out TimeZoneInfo timeZoneInfo)
        {
            if (string.IsNullOrWhiteSpace(ianaTimeZoneID))
            {
                timeZoneInfo = null!;
                return false;
            }

            if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaTimeZoneID, out string? windowsName))
            {
                timeZoneInfo = null!;
                return false;
            }

            try
            {
                timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(windowsName);
                return true;
            }
            catch (TimeZoneNotFoundException)
            {
                timeZoneInfo = null!;
                return false;
            }
            catch (InvalidTimeZoneException)
            {
                timeZoneInfo = null!;
                return false;
            }
        }
    }
}
