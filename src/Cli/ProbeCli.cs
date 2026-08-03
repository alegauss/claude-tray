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
/// <para><b>The live reading is recorded, not just printed (T212).</b> It used to be dumped to the console
/// and dropped, because <c>HeaderProbe.Record</c> was reached from the tray's two poll paths alone — so the
/// one command whose entire purpose is capturing what the API said threw away what it had just captured,
/// and the reading that settled T181 survived only because a person copied it into a commit. The call is
/// made against the same profile whose log was printed, and filed under that profile's key.</para>
///
/// <para><c>--probe --live</c> skips <em>reading</em> the log, not writing to it: a flag asking for the
/// freshest reading is the last one that should drop it. <c>--probe --all</c> reads every profile's log,
/// and still takes its live reading from the monitored one.</para>
/// </summary>
internal static class ProbeCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        bool liveOnly = args.Contains("--live");
        bool all = args.Contains("--all");

        Console.WriteLine("Rate-limit headers, verbatim. Quota metadata only — no message content, no token.");
        Console.WriteLine();

        Settings settings = Settings.Load();
        List<ClaudeInfo> discovered = ClaudeAccount.Discover(settings.Profiles);
        // The profile the live call is made against and filed under. Resolved once and used for both, so
        // the reading cannot land in another account's log — and so the log printed above it is the log the
        // reading joins, which was already not guaranteed while the call went to the process's default dir.
        ClaudeInfo target = ClaudeAccount.PickMonitored(discovered, settings.MonitoredConfigDir)
                            ?? ClaudeAccount.Read();
        string targetKey = ProfileStore.KeyFor(target);

        if (!liveOnly)
        {
            List<ClaudeInfo> profiles = all ? discovered : new List<ClaudeInfo> { target };
            var spread = new List<(string Profile, IReadOnlyList<ProbeEntry> Log)>();

            foreach (ClaudeInfo p in profiles)
            {
                string key = ProfileStore.KeyFor(p);
                List<ProbeEntry> log = HeaderProbe.Load(key);
                if (log.Count > 0) spread.Add((p.Label ?? key, log));
                Console.WriteLine($"== {p.Label ?? key} — {log.Count} recorded reading(s)");
                if (log.Count == 0)
                {
                    // Not "nothing has changed yet": the first reading is always kept, deliberately, as the
                    // baseline the rest are changes from. An empty log means nothing has polled this
                    // profile since the probe shipped, which is a different fact and the actionable one.
                    Console.WriteLine("   nothing on file. The first reading is always kept, so this profile");
                    Console.WriteLine("   has not been polled since the probe shipped — run the tray, or (for");
                    Console.WriteLine("   the monitored profile) the live call below files one now.");
                    Console.WriteLine();
                    continue;
                }
                Vocabulary(log, indent: "   ");
                Presence(log, indent: "   ");
                foreach (ProbeEntry e in log) Dump(e.T, e.Headers, indent: "   ");
                Console.WriteLine();
            }

            Spread(spread, indent: "   ");
        }

        Console.WriteLine($"== live, right now — {target.Label ?? targetKey}");
        UsageData d = await new ApiClient(target.ConfigDir).FetchAsync();
        if (d.RateHeaders.Count == 0)
        {
            Console.WriteLine($"   no rate-limit headers came back{(d.Error is { } e ? $": {e}" : "")}");
            return 1;
        }
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Dump(now, d.RateHeaders, indent: "   ");

        // Keep it. A reading taken and printed is a reading lost the moment the console scrolls.
        bool kept = HeaderProbe.Record(targetKey, now, d.RateHeaders);
        Console.WriteLine();
        Console.WriteLine(kept
            ? "   recorded — this shape was not on file."
            : "   not recorded: the shape has not moved since the last reading on file.");

        Console.WriteLine(d.HasExtra
            ? "   The overage header is present. T181 measured the window; what 100% of it *amounts to* is\n"
              + "   still unstated by any header — see IMPROVEMENTS §XVIII."
            : "   No overage header on this response, so this account has no extra-usage window to read.");
        return 0;
    }

    /// <summary>What each categorical header has ever said, before the readings it was derived from (T210).
    ///
    /// <para>The dump below it is the evidence and stays verbatim; this is the question actually being
    /// asked of it. Three headers here are parsed by nothing — <c>unified-status</c>, <c>unified-reset</c>
    /// and <c>unified-representative-claim</c> — and the reason is that their vocabulary is one value from
    /// one account, so any mapping written now has a default arm nobody has seen. Whether that is still
    /// true is a fact about the log, and it belongs at the top of the log rather than in a person's
    /// eyesight across five hundred readings of fourteen headers.</para></summary>
    private static void Vocabulary(List<ProbeEntry> log, string indent)
    {
        List<HeaderVocab> vocab = HeaderProbe.Vocabulary(log);
        if (vocab.Count == 0) return;

        int moved = vocab.Count(v => v.Moved);
        Console.WriteLine($"{indent}vocabulary — every value each categorical header has taken across "
                          + $"{log.Count} reading(s)");
        int width = vocab.Max(v => v.Values.Max(x => x.Value.Length));
        foreach (HeaderVocab h in vocab)
        {
            Console.WriteLine($"{indent}  {h.Name}{(h.Moved ? $"   ** {h.Values.Count} values **" : "")}");
            foreach (VocabValue v in h.Values)
                Console.WriteLine($"{indent}    {v.Value.PadRight(width)}  {v.Count,4}x   "
                                  + $"{When(v.First)}{(v.Last > v.First ? $" -> {When(v.Last)}" : "")}");
        }
        Console.WriteLine();
        // Naming what a single value costs is the point: it is not a shortage of readings, it is the reason
        // three headers stay unparsed, and the line has to say which of the two states the log is in.
        Console.WriteLine(moved > 0
            ? $"{indent}  {moved} header(s) have taken more than one value — the second reading\n"
              + $"{indent}  IMPROVEMENTS §XVIII.9 and §XVIII.10 are waiting on. Read them beside the design."
            : $"{indent}  Every categorical header has exactly one value on file, so its vocabulary is still\n"
              + $"{indent}  one sample from one account. A mapping written now would have a default arm\n"
              + $"{indent}  nobody has seen — see IMPROVEMENTS §XVIII.9.");
        Console.WriteLine();
    }

    /// <summary>The names this profile is not always sent (T211). Silent when every reading carried every
    /// name, which is the uninteresting case and the common one.</summary>
    private static void Presence(List<ProbeEntry> log, string indent)
    {
        List<HeaderPresence> intermittent = HeaderProbe.Presence(log).Where(p => p.Intermittent).ToList();
        if (intermittent.Count == 0) return;

        Console.WriteLine($"{indent}  not sent on every reading — a name's absence is a reading too");
        int width = intermittent.Max(p => p.Name.Length);
        foreach (HeaderPresence p in intermittent)
            Console.WriteLine($"{indent}    {p.Name.PadRight(width)}  {p.Readings} of {p.Total}");
        Console.WriteLine();
    }

    /// <summary>What each profile is sent and what it is not (T211).
    ///
    /// <para>The two accounts on this machine are the two samples, so the comparison cannot be made inside
    /// either log — and printing both logs in full, which is what <c>--all</c> did, left the diff to
    /// whoever was reading. Only the divergent names are listed: a name every profile is sent is the
    /// uninteresting case, and it is the one that fills the screen.</para></summary>
    private static void Spread(List<(string Profile, IReadOnlyList<ProbeEntry> Log)> logs, string indent)
    {
        if (logs.Count < 2) return;   // a spread over one profile is that profile's own vocabulary

        List<HeaderSpread> spread = HeaderProbe.Spread(logs);
        List<HeaderSpread> divergent = spread.Where(h => h.Divergent).ToList();
        Console.WriteLine($"== across {logs.Count} profiles — {spread.Count - divergent.Count} name(s) sent "
                          + $"to all, {divergent.Count} to only some");
        if (divergent.Count == 0)
        {
            Console.WriteLine($"{indent}every profile is sent the same set of names.");
            Console.WriteLine();
            return;
        }

        int width = logs.Max(l => l.Profile.Length);
        foreach (HeaderSpread h in divergent)
        {
            Console.WriteLine($"{indent}{h.Name}");
            foreach (ProfileValue v in h.ByProfile)
                Console.WriteLine($"{indent}  {v.Profile.PadRight(width)}  {v.Value ?? "— never sent"}");
        }
        Console.WriteLine();
        // Saying what it does *not* license matters more here than what it shows: a name sent to one
        // account and not another is the shape of a permission, and a permission this app has never seen
        // refused is one it must not describe.
        Console.WriteLine($"{indent}A name sent to one account and never to another is a reading in itself:");
        Console.WriteLine($"{indent}whatever it announces is conditional. What the condition *is* stays");
        Console.WriteLine($"{indent}unmeasured, so no string in this app may explain it — see IMPROVEMENTS §XVIII.");
        Console.WriteLine();
    }

    private static string When(double t) =>
        DateTimeOffset.FromUnixTimeSeconds((long)t).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

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
