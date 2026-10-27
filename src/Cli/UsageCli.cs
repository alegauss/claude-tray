using System.Text.Json;

namespace ClaudeTray;

/// <summary>
/// <c>--usage [--json]</c>: where the quota stands, for a script as much as for a person (T391).
///
/// <para><b>Read from the store, never fetched.</b> The answer is the monitored profile's newest reading
/// in <c>usage-history.jsonl</c> — what the tray last measured, or took from the status line (T390) — so a
/// git hook or a status bar asking every few seconds spends no API call. That makes its age part of the
/// answer: a reading older than <see cref="StaleSeconds"/> is reported with its age and exits
/// <see cref="ExitNothing"/>, never as current.</para>
///
/// <para><b>The exit code is the reading's, decided by the icon's own arithmetic.</b>
/// <see cref="BurnTracker.PaceLine"/> is what turns the icon red and what <see cref="PaceAlarm"/>
/// announces, so a script branching on <see cref="ExitNear"/> and the icon cannot disagree. The four
/// codes follow Claude Code Usage Monitor's (<c>0</c>, <c>10</c>, <c>11</c>, <c>20</c>) because a script
/// written against one should not have to learn a second set; <c>1</c> stays what every read-out here
/// means by it, that the read could not start (T261).</para>
///
/// <para><b>The JSON is a contract</b> from the release that ships it, so it carries
/// <see cref="Schema"/>, and it holds nothing the tooltip does not: numbers, times, verdicts and the
/// profile's label. No token, no account address.</para>
/// </summary>
internal static class UsageCli
{
    /// <summary>Bumped when a field is renamed, removed or changes meaning; adding one does not.</summary>
    public const int Schema = 1;

    public const int ExitOk = 0;
    public const int ExitNear = 10;
    public const int ExitAtLimit = 11;
    public const int ExitNothing = 20;

    /// <summary>Past this age a reading is not the quota now: the tray polls every few minutes and the
    /// status line on every response, so an hour without either means nothing is measuring.</summary>
    public const double StaleSeconds = 3600;

    /// <summary>One window as the read-out states it. <c>ExhaustAt</c> is when it reaches 100% at the
    /// pace so far, or null when that lands after the reset (the window resets first) or never.</summary>
    internal sealed record Window(string Key, double Used, double ResetsAt, bool Expired,
                                  Projection Verdict, double? ExhaustAt);

    internal sealed record Report(string? Profile, double Now, double? ReadAt, bool Stale,
                                  IReadOnlyList<Window> Windows, double? Extra, bool? ExtraInUse)
    {
        public double? Age => ReadAt is { } t ? Now - t : null;
    }

    /// <summary>The report over one reading, pure: no clock and no file, so --selftest drives every
    /// state.</summary>
    public static Report Compose(string? profile, UsageSample? reading, double now)
    {
        if (reading is not { } s) return new Report(profile, now, null, false, Array.Empty<Window>(), null, null);

        var windows = new List<Window>();
        foreach ((string key, double used, double reset) in new[] { ("5h", s.Util5h, s.Reset5h), ("7d", s.Util7d, s.Reset7d) })
        {
            // A reset already behind us means the number is last cycle's: the window has rolled over and
            // nothing measured what it holds now.
            if (reset <= now)
            {
                windows.Add(new Window(key, used, reset, true, Projection.Unknown, null));
                continue;
            }
            (Projection verdict, double exhaust) = BurnTracker.PaceLine(used, reset, now, BurnTracker.WindowSeconds(key));
            double? at = used >= 1.0 ? now
                       : double.IsFinite(exhaust) && now + exhaust < reset ? now + exhaust
                       : null;
            windows.Add(new Window(key, used, reset, false, verdict, at));
        }
        return new Report(profile, now, s.T, now - s.T > StaleSeconds, windows, s.Extra, s.InUse);
    }

    /// <summary>The code a script branches on: nothing to judge, at a limit, near one, or fine.</summary>
    public static int ExitCode(Report r)
    {
        List<Window> live = r.Windows.Where(w => !w.Expired).ToList();
        if (r.ReadAt is null || r.Stale || live.Count == 0) return ExitNothing;
        if (live.Any(w => w.Used >= 1.0)) return ExitAtLimit;
        if (live.Any(w => w.Verdict == Projection.Danger || w.Used >= StatusLineFeed.NearLimit)) return ExitNear;
        return ExitOk;
    }

    public static string Json(Report r)
    {
        var o = new Dictionary<string, object?>
        {
            ["schema"] = Schema,
            ["profile"] = r.Profile,
            ["now"] = (long)r.Now,
            ["readAt"] = r.ReadAt is { } t ? (long)t : null,
            ["ageSeconds"] = r.Age is { } a ? (long)a : null,
            ["stale"] = r.Stale,
            ["windows"] = r.Windows.ToDictionary(w => w.Key, w => (object?)new Dictionary<string, object?>
            {
                ["used"] = Math.Round(w.Used, 4),
                ["resetsAt"] = (long)w.ResetsAt,
                ["expired"] = w.Expired,
                ["verdict"] = w.Expired ? "unknown" : w.Verdict.ToString().ToLowerInvariant(),
                ["exhaustAt"] = w.ExhaustAt is { } x ? (long)x : null,
            }),
            // Absent is not zero (T179): null here is a reading that carried no overage figure.
            ["extra"] = r.Extra is { } e ? Math.Round(e, 4) : null,
            ["extraInUse"] = r.ExtraInUse,
            ["exitCode"] = ExitCode(r),
        };
        return JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true });
    }

    public static IEnumerable<string> Text(Report r)
    {
        if (r.ReadAt is null)
        {
            yield return "no reading on file for " + (r.Profile ?? "this profile") +
                         " — the tray has not polled it yet";
            yield break;
        }
        yield return $"{r.Profile}: read {Ago(r.Age ?? 0)} ago" + (r.Stale ? " — STALE, not the quota now" : "");
        foreach (Window w in r.Windows)
        {
            // Digits only: the sentence around it is English, as every read-out here is, and a weekday name
            // would follow the display language into the middle of it.
            string resets = DateTimeOffset.FromUnixTimeSeconds((long)w.ResetsAt).ToLocalTime()
                                          .ToString("yyyy-MM-dd HH:mm", L.DateCulture);
            if (w.Expired)
            {
                yield return $"  {w.Key}  {Nums.Pct(w.Used),4}  reset at {resets} has passed — unknown until the next reading";
                continue;
            }
            string pace = w.Verdict == Projection.Danger ? "AHEAD of pace" : "on pace";
            string runsOut = w.ExhaustAt is { } x ? $", 100% in {Ago(x - r.Now)}" : "";
            yield return $"  {w.Key}  {Nums.Pct(w.Used),4}  resets {resets}  {pace}{runsOut}";
        }
        if (r.Extra is not null || r.ExtraInUse is true)
            yield return $"  extra usage {Nums.Pct(r.Extra ?? 0)}" + (r.ExtraInUse is true ? " — spending past the quota" : "");
    }

    private static string Ago(double seconds) => seconds switch
    {
        < 90 => $"{Math.Max(0, (int)seconds)}s",
        < 5400 => $"{(int)Math.Round(seconds / 60)}m",
        < 172800 => $"{Nums.Of(seconds / 3600, "0.#")}h",
        _ => $"{Nums.Of(seconds / 86400, "0.#")}d",
    };

    public static int Run(string[] args)
    {
        Settings settings = Settings.Load();
        ClaudeInfo profile = ClaudeAccount.PickMonitored(ClaudeAccount.Discover(settings.Profiles),
                                                         settings.MonitoredConfigDir)
                             ?? ClaudeAccount.Read();
        Report r = Compose(profile.Label, UsageHistory.Latest(ProfileStore.KeyFor(profile)),
                           DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (args.Contains("--json")) Console.WriteLine(Json(r));
        else foreach (string line in Text(r)) Console.WriteLine(line);
        return ExitCode(r);
    }
}
