using System.Diagnostics;

namespace ClaudeTray;

/// <summary>The `--profiles` family: every Claude Code profile this machine exposes. Split out of `Program.cs` by T132 —
/// moved verbatim.</summary>
internal static class ProfilesCli
{
    // `--profiles [dir...]`: the config dirs ClaudeAccount.Discover finds, in order, with what each
    // one identifies. Read-only, like the page it backs. The account uuid is truncated — it is the
    // dedupe key, and printing it whole serves nobody.
    internal static void PrintProfiles(string[] args)
    {
        // `--check` also asks the CLI itself (`claude auth status --json`) per profile — the same
        // authoritative confirmation the page's Check button runs, and the only way to exercise it
        // without clicking.
        bool check = args.Any(a => a is "--check");
        string[] extra = args.Where(a => !a.StartsWith("--")).ToArray();
        // Discovered through the tray's own registered list, and not through the config-dir overload
        // (T232). That one skips the typed-label pass, so this read-out was answering with the *derived*
        // label — "Personal" — for a profile every surface a user sees calls "Pessoal". A read-out of the
        // app's state has to reach it by the app's route, or it is a second computation that agrees only
        // while nobody has renamed anything. A dir named on the command line is an extra on top of the
        // registered ones, carrying no label, so it still reads as whatever the files identify it as.
        List<ClaudeProfile> registered = Settings.Load().Profiles
            .Concat(extra.Select(d => new ClaudeProfile { ConfigDir = d })).ToList();
        // Timed because the tray now re-runs this on every menu open (T137): it has to stay cheap.
        var discovery = System.Diagnostics.Stopwatch.StartNew();
        List<ClaudeInfo> profiles = ClaudeAccount.Discover(registered);
        discovery.Stop();
        Console.WriteLine($"Claude Code profiles: {profiles.Count}"
                          + (extra.Length > 0 ? $" ({extra.Length} extra dir(s) passed in)" : "")
                          + $"   discovery {discovery.Elapsed.TotalMilliseconds:0.0}ms");
        Console.WriteLine();
        foreach (ClaudeInfo p in profiles)
        {
            Console.WriteLine($"  {p.Label}{(p.IsDefault ? "  [default]" : "")}");
            Console.WriteLine($"    dir      {p.ConfigDir}");
            Console.WriteLine($"    plan     {p.Plan ?? "-"}{(p.PlanTier is { } t ? $"  ({t})" : "")}");
            Console.WriteLine($"    account  {(p.AccountUuid is { Length: > 8 } u ? u[..8] + "…" : p.AccountUuid ?? "-")}"
                              + $"   org {p.OrgName ?? "-"}   signed in {(p.TokenExpires is { } e ? e.ToString("g") : "-")}");
            Console.WriteLine($"    projects {p.ProjectCount}   cli {p.CliVersion ?? "-"}   install {p.InstallMethod ?? "-"}");
            Console.WriteLine($"    auth     {p.Auth}{(p.AuthSource is { } s ? $" (from {s})" : "")}"
                              + (p.CountsAgainstSubscription
                                  ? "   counts against the subscription"
                                  : "   !! does NOT count against the subscription the tray measures"));
            if (check)
            {
                (AuthMethod auth, string? source, string? error) =
                    ClaudeAccount.QueryAuthStatusAsync(p.ConfigDir).GetAwaiter().GetResult();
                Console.WriteLine(error is null
                    ? $"    confirmed {auth}{(source is { } cs ? $" (from {cs})" : "")}"
                      + (auth == p.Auth ? "   — matches the local reading" : "   — DIFFERS from the local reading")
                    : $"    confirmed -   (claude auth status failed: {error})");
            }
            // Exactly what the tray submenu would do for this profile — same helpers the click uses.
            string workDir = TrayContext.WorkDirFor(p, Settings.Load().ClaudeCodeDirectory);
            Console.WriteLine($"    menu     \"{(p.HasCredentialsFile ? p.Label : L.T("menu.profileNoLogin", p.Label))}\""
                              + $" -> {TrayContext.LaunchCommandFor(p)}");
            // What the launch does to the config dir — the three cases, because "nothing" and "remove
            // the inherited one" look identical from the outside and mean opposite things (T144).
            Console.WriteLine("             "
                              + ClaudeAccount.ActionFor(p.ConfigDir) switch
                              {
                                  ClaudeAccount.ConfigDirAction.Set => $"CLAUDE_CONFIG_DIR={p.ConfigDir}",
                                  ClaudeAccount.ConfigDirAction.Unset =>
                                      "CLAUDE_CONFIG_DIR removed — inherited "
                                      + $"{Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")} would select another profile",
                                  _ => "no CLAUDE_CONFIG_DIR — this is the dir a bare `claude` already uses",
                              }
                              + $"   cwd {workDir}"
                              + (Directory.Exists(workDir) ? "" : "  (missing — OS default applies)"));
            // The identity band the icon wears while this profile owns it (T147) — printed so the
            // mapping can be checked without driving the taskbar.
            int slot = TrayContext.AccentSlotFor(profiles, p);
            Console.WriteLine("             " + (slot < 0
                ? "no accent band — only one profile, so there is no \"whose number is this?\" to answer"
                : $"accent band {IconRenderer.AccentName(slot)}"));
            Console.WriteLine();
        }
        Settings settings = Settings.Load();
        ClaudeInfo? monitored = ClaudeAccount.PickMonitored(profiles, settings.MonitoredConfigDir);
        int polled = profiles.Count(p => p.CountsAgainstSubscription);
        Console.WriteLine(profiles.Count == 1
            ? "Only one profile — the Settings picker stays hidden, Open Claude Code stays a plain command,"
              + " and the Profile submenu is hidden."
            : TrayContext.LaunchIsSubmenu(settings, profiles.Count)
                ? $"Open Claude Code becomes a submenu with {profiles.Count} entries; the Profile submenu is shown."
                : "Open Claude Code stays a plain command — the picked profile is the environment's, so every"
                  + " entry would open the same session (T146); the Profile submenu is shown.");
        // What a pick in that submenu actually reaches, and the toggle the submenu now carries for the
        // other half (T171) — printed so the scope the menu states can be read without hovering it.
        Console.WriteLine($"a pick reaches: {(settings.SyncEnvironmentProfile ? "the tray and the Windows user environment" : "the tray only")}"
                          + $"   \"{L.T("menu.profileEnvSync")}\" {(settings.SyncEnvironmentProfile ? "[x]" : "[ ]")}"
                          + "   (sessions started from now on; running ones keep theirs)");
        Console.WriteLine($"icon follows: {monitored?.Label ?? "-"}  ({monitored?.ConfigDir ?? "-"})");
        // The other question, and the one `/usage` answers (T172): whose numbers the icon draws is not
        // which profile the environment selects, and the two are printed together so a disagreement is
        // read here rather than discovered inside a session.
        ClaudeInfo? envProfile = EnvironmentProfile.Selected(profiles);
        string envDir = EnvironmentProfile.EffectiveConfigDir();
        bool agrees = envProfile is not null && monitored is not null
                      && ClaudeAccount.SamePath(envProfile.ConfigDir, monitored.ConfigDir);
        // Said before the reading it qualifies, not after: a read-out describing a fixture without
        // saying so is the same defect the fixture exists to expose, one level up (T231).
        if (EnvironmentProfile.IsSampled)
            Console.WriteLine("SAMPLED ENVIRONMENT (--sample-env): the three lines below answer off a "
                              + "fixture, and this process writes no variable.");
        Console.WriteLine($"environment selects: {envProfile?.Label ?? "none of these"}  ({envDir})"
                          + (EnvironmentProfile.Current() is null ? "   CLAUDE_CONFIG_DIR not set - the default applies" : "")
                          + (agrees ? "   - agrees with the icon" : "   !! DIFFERS from the icon"));
        // The bookkeeping a write leaves behind, which is the only part of it that outlives the process
        // that queued it (T173). "Owned" is the tray's claim that it took the variable over; the line
        // above is what the registry says about that claim, and the two disagreeing is a write that was
        // accepted and never landed.
        Console.WriteLine($"tray owns the variable: {(settings.EnvironmentProfileOwned ? "yes" : "no")}"
                          + $"   restores to {settings.EnvironmentProfileRestore ?? "(unset)"}"
                          + (EnvironmentProfile.Last is { } last
                              ? $"   last write this process: {(last.Landed ? "landed" : "DID NOT LAND")}"
                                + (last.Error is { } why ? $" ({why})" : "")
                              : ""));
        Console.WriteLine($"polled every interval: {polled} of {profiles.Count}"
                          + (polled < profiles.Count
                              ? "  (a profile off the subscription has no quota window to read)" : ""));

        // Auto-follow (T126): when each profile last had a turn, and where the icon would move to. The
        // probe reads transcript *timestamps* only, so this also states its cost.
        Console.WriteLine();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        List<ProfileActivity.Reading> readings = ProfileActivity.Read(profiles);
        sw.Stop();
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Console.WriteLine($"auto-follow: {(settings.FollowActiveProfile ? "on" : "off")}"
                          + $"   window {ProfileActivity.FollowWindowSeconds / 60:0}min"
                          + $"   probe {sw.Elapsed.TotalMilliseconds:0.0}ms for {profiles.Count} profile(s)");
        foreach (ProfileActivity.Reading r in readings)
        {
            string age = r.LastTurnUnix <= 0
                ? "never"
                : $"{(nowUnix - r.LastTurnUnix) / 60.0:0.0} min ago";
            Console.WriteLine($"  {r.Profile.Label,-24} last turn {age,-16}"
                              + (!r.Followable
                                  ? "not followable (no subscription quota to read, or no credentials)"
                                  // T365: the reading is another profile's as much as this one's, so it
                                  // is not evidence — and the shared directory is what a reader needs to
                                  // see, since neither profile's own path says it is a link.
                                  : r.SharesTree
                                      ? $"shares its transcripts, so not evidence — {r.Tree}"
                                      : ProfileActivity.Live(r, nowUnix) ? "candidate"
                                      : r.LastTurnUnix <= 0 ? "followable, but never used"
                                      : "followable, but no turn inside the window"));
        }
        ClaudeInfo? active = ProfileActivity.Pick(readings, nowUnix, 0);
        Console.WriteLine(active is null
            ? "would follow: nobody — "
              + (readings.Any(r => r.SharesTree)
                  ? "the profiles sharing a directory above are not evidence about each other, so the "
                    + "icon stays where it was put."
                  : "no recent turn in a followable profile, so the icon stays put.")
            : $"would follow: {active.Label}  ({active.ConfigDir})"
              + (settings.FollowActiveProfile ? "" : "  — if auto-follow were on"));
        if (profiles.Count > 1)
            Console.WriteLine("to share one setup between two of these: --link-profiles 0 1");
    }

    /// <summary>
    /// <c>--link-profiles &lt;primary&gt; &lt;secondary&gt; [out=&lt;path&gt;]</c>: the script that makes
    /// two profiles one setup (T367). Each side is an index into the <c>--profiles</c> list or a config-dir
    /// path outright, so the pair can be named the way the read-out above prints them.
    ///
    /// <para>It prints the plan and the script, and writes the file only when asked. The app has no write
    /// path into <c>~\.claude</c> and this is not one: what lands on disk is a <c>.ps1</c> in a directory
    /// the caller named, and reading it before running it is the point (§XCI).</para>
    /// </summary>
    internal static void PrintLinkScript(string[] args)
    {
        string? outPath = args.FirstOrDefault(a => a.StartsWith("out=", StringComparison.OrdinalIgnoreCase))?[4..];
        string[] sides = args.Where(a => !a.StartsWith("--") && !a.Contains('=')).ToArray();
        if (sides.Length != 2)
        {
            ReadOut.Failed("--link-profiles takes two profiles: an index from --profiles, or a config dir. "
                           + $"Got {sides.Length}.");
            return;
        }

        List<ClaudeInfo> profiles = ClaudeAccount.Discover(Settings.Load().Profiles);
        // An index resolves through the same discovery order --profiles prints, so "0 1" means what the
        // read-out above just showed. Anything else is taken as a path, which is what a config dir that is
        // not registered here has to be given as.
        (string dir, string label)? Side(string arg) =>
            int.TryParse(arg, out int i)
                ? i >= 0 && i < profiles.Count ? (profiles[i].ConfigDir, profiles[i].Label) : null
                : (arg, ClaudeAccount.SamePath(arg, profiles.FirstOrDefault()?.ConfigDir ?? "\0")
                    ? profiles[0].Label
                    : Path.GetFileName(Path.TrimEndingDirectorySeparator(arg)));

        if (Side(sides[0]) is not { } a || Side(sides[1]) is not { } b)
        {
            ReadOut.Failed($"no such profile index — --profiles lists {profiles.Count}, so an index is 0..{profiles.Count - 1}.");
            return;
        }

        ProfileLink.Plan plan = ProfileLink.For(a.dir, b.dir, a.label, b.label);
        if (ReadOut.Failed(plan.Error)) return;

        // The plan as a table first, because the script is long and the decision a reader makes is per
        // entry: what is merged, what is adopted whole, what is deliberately withheld.
        Console.WriteLine($"keeps its files   {plan.PrimaryLabel}  ({plan.PrimaryDir})");
        Console.WriteLine($"becomes links     {plan.SecondaryLabel}  ({plan.SecondaryDir})");
        Console.WriteLine();
        foreach (ProfileLink.Step s in plan.Steps)
        {
            bool links = s.Entry.Verdict is ProfileLink.Verdict.Merge or ProfileLink.Verdict.Adopt;
            // The link kind is blank for an entry that is never linked — printing "symlink" beside
            // `.credentials.json` states the one thing this whole table exists to deny.
            Console.WriteLine($"  {s.Entry.Name,-20} {s.Entry.Verdict,-8} "
                              + $"{(!links ? "" : s.Entry.IsDirectory ? "junction" : "symlink"),-9}"
                              + (!links ? s.Entry.Why
                                  : s.AlreadyLinked ? "already linked"
                                  : !s.OnPrimary ? "not on the primary side — skipped"
                                  : s.Entry.Union == ProfileLink.Union.Lines ? "union by line"
                                  : s.Entry.Union == ProfileLink.Union.Entries ? $"union by {s.Entry.Unit}"
                                  : "adopted whole"));
        }
        Console.WriteLine();
        Console.WriteLine($"{plan.Acting.Count()} entry(ies) would be linked"
                          + (plan.NeedsSymlink
                              ? "; one of them is a file, so the script refuses without Developer Mode"
                              : "; every link is a junction, so no privilege is needed"));
        Console.WriteLine();

        string script = ProfileLink.Script(plan);
        if (outPath is { Length: > 0 })
        {
            // UTF-8 *with* a BOM, which is not the usual preference here and is not stylistic: Windows
            // PowerShell 5.1 reads a BOM-less .ps1 as ANSI, and the two config-dir paths are pasted into
            // the script verbatim — a user folder with an accent in it would be mangled into a path that
            // does not exist, by the one part of this file nothing can normalise. The prose is ASCII so a
            // copy-paste of the printed form is safe too, which is what `--selftest` holds.
            using (var fs = OutFile.Create(outPath))
            using (var w = new StreamWriter(fs, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
                w.Write(script);
            Console.WriteLine($"wrote {Path.GetFullPath(outPath)} — read it, then run it with -Apply.");
        }
        else
        {
            Console.WriteLine(script);
            Console.WriteLine("# (pass out=<path.ps1> to write this instead of printing it)");
        }
    }
}
