namespace Niobium.Billing.Functions
{
    internal class TimeZoneInfoHelper
    {
        public static TimeZoneInfo ParseTimeZoneFromIANA(string ianaTimeZoneID)
        {
            if (!TryParseTimeZoneFromIANA(ianaTimeZoneID, out var result))
            {
                throw new ArgumentException($"Invalid IANA time zone ID: {ianaTimeZoneID}", nameof(ianaTimeZoneID));
            }

            return result;
        }

        public static bool TryParseTimeZoneFromIANA(string ianaTimeZoneID, out TimeZoneInfo timeZoneInfo)
        {
            if (string.IsNullOrWhiteSpace(ianaTimeZoneID))
            {
                timeZoneInfo = null!;
                return false;
            }

            if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaTimeZoneID, out var windowsName))
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
