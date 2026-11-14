using System.Globalization;

namespace Niobium.Invoicing.Testing;

/// <summary>Builds a read-only test telemetry query and admits only fixed labels and numeric aggregates.</summary>
internal static class DeployedHostDiagnostics
{
    internal sealed record Counts(string Window, string Version, long Exceptions, long OutOfMemory,
        long WorkerExit, long Restart, long Timeout, long ServerErrors, long ClientErrors,
        long SlowRequests, long Requests, long SelectedTraces);

    internal static string Query(DateTimeOffset started, DateTimeOffset ended, string version)
    {
        if (ended < started || ended - started > TimeSpan.FromHours(1))
            throw new ArgumentException("A bounded test-suite window is required.");
        if (!System.Version.TryParse(version, out _) || version.Split('.').Length != 3 ||
            version.Any(value => !Char.IsAsciiDigit(value) && value != '.'))
            throw new ArgumentException("The three-part workflow version is required.");
        string first = started.AddHours(-2).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        string last = ended.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        string suite = started.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        return $$"""
            union
                (exceptions | where timestamp between (datetime({{first}}) .. datetime({{last}}))
                    | project timestamp, application_Version, source="exception", text=strcat(type, " ", outerType), code=0, elapsed=0.0),
                (traces | where timestamp between (datetime({{first}}) .. datetime({{last}}))
                    | where severityLevel >= 3 or message has_any ("OutOfMemory", "OOM", "memory", "Restarting", "restarting", "exited")
                    | project timestamp, application_Version, source="trace", text=message, code=0, elapsed=0.0),
                (requests | where timestamp between (datetime({{first}}) .. datetime({{last}}))
                    | project timestamp, application_Version, source="request", text="", code=toint(resultCode), elapsed=todouble(duration))
            | extend window=iff(timestamp >= datetime({{suite}}), "suite", "preceding-two-hours"),
                version=iff(application_Version == "{{version}}-prerelease", "current", "other-or-unspecified")
            | summarize exceptions=countif(source == "exception"),
                outOfMemory=countif(text contains "OutOfMemory" or text contains "out of memory" or text has "OOM"),
                workerExit=countif(text contains "worker" and text contains "exited"),
                restart=countif(text contains "restart"),
                timeout=countif(text contains "timeout" or text contains "timed out"),
                serverErrors=countif(source == "request" and code >= 500),
                clientErrors=countif(source == "request" and code between (400 .. 499)),
                slowRequests=countif(source == "request" and elapsed >= 30000),
                requests=countif(source == "request"), selectedTraces=countif(source == "trace") by window, version
            | project window, version, exceptions, outOfMemory, workerExit, restart, timeout,
                serverErrors, clientErrors, slowRequests, requests, selectedTraces
            | order by window asc, version asc
            """;
    }

    internal static IReadOnlyList<Counts> Parse(string response)
    {
        if (response.Length > 4096)
            throw new FormatException("The aggregate response is too large.");
        string[] rows = response.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rows.Length > 4)
            throw new FormatException("The aggregate response has too many groups.");
        List<Counts> result = [];
        HashSet<(string, string)> groups = [];
        foreach (string row in rows)
        {
            string[] cells = row.Split('\t');
            if (cells.Length != 12 || cells[0] is not ("suite" or "preceding-two-hours") ||
                cells[1] is not ("current" or "other-or-unspecified"))
                throw new FormatException("The aggregate response has an unexpected shape.");
            bool unique = groups.Add((cells[0], cells[1]));
            if (!unique)
                throw new FormatException("The aggregate response repeats a group.");
            long[] values = new long[10];
            for (int i = 0; i < values.Length; i++)
            {
                bool parsed = long.TryParse(cells[i + 2], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]);
                if (!parsed || values[i] < 0)
                    throw new FormatException("The aggregate response must contain nonnegative counts.");
            }
            result.Add(new(cells[0], cells[1], values[0], values[1], values[2], values[3], values[4],
                values[5], values[6], values[7], values[8], values[9]));
        }
        return result;
    }
}
