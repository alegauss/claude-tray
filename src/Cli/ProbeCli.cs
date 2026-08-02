namespace ClaudeTray;

/// <summary>
/// <c>--probe</c>: the rate-limit headers verbatim — what the API actually said, rather than this app's
/// reading of four of them (T181).
///
/// <para>Prints the capture log first (every shape change the tray has recorded, per profile), then makes
/// one live call and prints that response's headers in full. The log is the part that matters: the
/// question is what the overage percentage denominates, and only an account already in overage can answer
/// it — the live call shows today, the log shows the transition whenever it happened.</para>
///
/// <para><c>--probe --live</c> skips the log; <c>--probe --all</c> reads every profile's log.</para>
/// </summary>
internal static class ProbeCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        bool liveOnly = args.Contains("--live");
        bool all = args.Contains("--all");

        Console.WriteLine("Rate-limit headers, verbatim. Quota metadata only — no message content, no token.");
        Console.WriteLine();

        if (!liveOnly)
        {
            List<ClaudeInfo> profiles = all
                ? ClaudeAccount.Discover(Settings.Load().Profiles)
                : new List<ClaudeInfo> { ClaudeAccount.PickMonitored(ClaudeAccount.Discover(Settings.Load().Profiles), Settings.Load().MonitoredConfigDir) ?? ClaudeAccount.Read() };

            foreach (ClaudeInfo p in profiles)
            {
                string key = ProfileStore.KeyFor(p);
                List<ProbeEntry> log = HeaderProbe.Load(key);
                Console.WriteLine($"== {p.Label ?? key} — {log.Count} recorded reading(s)");
                if (log.Count == 0)
                {
                    Console.WriteLine("   nothing yet: the tray records a reading when the header shape first moves.");
                    Console.WriteLine();
                    continue;
                }
                foreach (ProbeEntry e in log) Dump(e.T, e.Headers, indent: "   ");
                Console.WriteLine();
            }
        }

        Console.WriteLine("== live, right now");
        UsageData d = await new ApiClient().FetchAsync();
        if (d.RateHeaders.Count == 0)
        {
            Console.WriteLine($"   no rate-limit headers came back{(d.Error is { } e ? $": {e}" : "")}");
            return 1;
        }
        Dump(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), d.RateHeaders, indent: "   ");

        Console.WriteLine();
        Console.WriteLine(d.HasExtra
            ? "   The overage header is present. What 100% of it would be is the open question — see IMPROVEMENTS §XVIII."
            : "   No overage header on this response, so this account is not the one that can answer T181.");
        return 0;
    }

    private static void Dump(double t, IReadOnlyDictionary<string, string> headers, string indent)
    {
        string when = DateTimeOffset.FromUnixTimeSeconds((long)t).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        Console.WriteLine($"{indent}{when}");
        var names = new List<string>(headers.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (string n in names)
            Console.WriteLine($"{indent}  {n}: {headers[n]}");
    }
}
