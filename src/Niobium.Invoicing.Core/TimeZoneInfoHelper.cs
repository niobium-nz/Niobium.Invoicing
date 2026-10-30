namespace Niobium.Invoicing;

/// <summary>
/// Resolves IANA timezone names through system timezone conversion data.
/// </summary>
internal class TimeZoneInfoHelper
{
    /// <summary>
    /// Resolves an IANA timezone through Windows-ID conversion and system timezone data.
    /// </summary>
    /// <param name="ianaTimeZoneID">IANA timezone identifier.</param>
    /// <returns>The resolved timezone.</returns>
    /// <exception cref="ArgumentException">The identifier is blank, cannot be converted, or cannot be resolved.</exception>
    public static TimeZoneInfo ParseTimeZoneFromIANA(string ianaTimeZoneID)
    {
        return !TryParseTimeZoneFromIANA(ianaTimeZoneID, out TimeZoneInfo? result)
            ? throw new ArgumentException($"Invalid IANA time zone ID: {ianaTimeZoneID}", nameof(ianaTimeZoneID))
            : result;
    }

    /// <summary>
    /// Attempts IANA-to-Windows conversion and system timezone resolution.
    /// </summary>
    /// <param name="ianaTimeZoneID">IANA timezone identifier.</param>
    /// <param name="timeZoneInfo">Resolved timezone on success; null on failure despite the non-nullable out annotation.</param>
    /// <returns>True when resolved; false for blank, unconvertible, missing or invalid timezone data.</returns>
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
