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
                                  : ProfileActivity.Live(r, nowUnix) ? "candidate"
                                  : r.LastTurnUnix <= 0 ? "followable, but never used"
                                  : "followable, but no turn inside the window"));
        }
        ClaudeInfo? active = ProfileActivity.Pick(readings, nowUnix, 0);
        Console.WriteLine(active is null
            ? "would follow: nobody — no recent turn in a followable profile, so the icon stays put."
            : $"would follow: {active.Label}  ({active.ConfigDir})"
              + (settings.FollowActiveProfile ? "" : "  — if auto-follow were on"));
    }
}
