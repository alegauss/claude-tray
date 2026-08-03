using System.Text.Json;

namespace ClaudeTray;

/// <summary>
/// Builds a throwaway pair of Claude Code config dirs — a personal Max 20x login and a Team seat — so
/// the System information page can be **published**. Every other window in this repo has a screenshot
/// in the README and on the site; this one could not have one, because masking hides the holder's name
/// and the local part of the address but the organization name and the mail domain *are* the reading,
/// and on a real machine the organization is somebody's client. Same reason
/// <see cref="ContextFixture"/> exists, one directory up.
///
/// <para>Two accounts, because they are the two layouts the page renders: one with no organization (the
/// org row collapses) and one with an organization plus a role. A third case — no OAuth account at all
/// — needs no fixture, since an empty directory produces it.</para>
///
/// <para>It plugs into the seam that already existed: <see cref="ClaudeAccount.Read(string)"/> reads any
/// config dir, so the fixture is parsed by the very code the page uses — not by a stub that could drift
/// from it. Nothing is written near the real configuration, and no token is written at all: the
/// credentials file carries only the three fields <see cref="ClaudeAccount"/> reads out of it.</para>
/// </summary>
internal static class AccountFixture
{
    /// <summary>Where the fixture profiles are built. Wiped and rebuilt on every call.</summary>
    public static string Root => SampleRoot.For("ClaudeTray-sample-accounts");

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Which stored week the personal profile gets, chosen with <c>week=</c> beside <c>--sample</c> — the
    /// way <see cref="StatsPreviews"/>'s variants choose what the chart is fed (T200).
    ///
    /// <para>It exists because the System page's extra-usage row has three branches and only two could be
    /// produced. <see cref="Spending"/> is T190's week and renders "Enabled — in use now (42%)".
    /// <see cref="Zero"/> is the branch that had never been on screen at all: extra usage enabled with
    /// nothing spent, which is the answer a worried user most wants to see. <see cref="Absent"/> is the
    /// no-reading path, which any empty profile already shows — named here so all three can be captured
    /// and compared at their rendered width rather than two of them.</para>
    ///
    /// <para>Deliberately not solved by flipping the Team seat's <c>hasExtraUsageEnabled</c>: a seat with
    /// extra usage off is a real reading, the published <c>system-account.png</c> documents it, and trading
    /// one unrendered state for another is not progress.</para>
    /// </summary>
    internal enum SampleWeek { Spending, Zero, Absent }

    /// <summary>Resolve the <c>week=</c> value, or null for a name that exists nowhere — refused with the
    /// catalogue printed, on the same rule as the preview tables (T186): a token that is not understood
    /// must not quietly select the default.</summary>
    internal static SampleWeek? ResolveWeek(string? word)
    {
        if (string.IsNullOrEmpty(word)) return SampleWeek.Spending;
        if (Enum.TryParse(word, ignoreCase: true, out SampleWeek parsed)) return parsed;

        Console.WriteLine($"unknown week '{word}'. Refusing rather than building the default one, which " +
                          "is how a screenshot of the wrong branch gets taken (T200).");
        Console.WriteLine();
        Console.WriteLine("weeks (week=<name> beside --sample), for the System page's extra-usage row:");
        Console.WriteLine("  spending   the included quota ran out and the allowance is paying: 'in use now (42%)'");
        Console.WriteLine("  zero       enabled, measured, nothing spent: 'not in use'");
        Console.WriteLine("  absent     no stored reading at all, so the row says only 'Enabled'");
        return null;
    }

    /// <summary>
    /// Create the two config dirs and read them back the way the page does. The first stands in for the
    /// profile a bare <c>claude</c> would land in, so the picker shows its "(default)" marking.
    /// </summary>
    public static List<ClaudeInfo> Build(DateTime nowUtc, SampleWeek week = SampleWeek.Spending)
    {
        string root = Root;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);

        // Named the way a real machine names them (~/.claude plus a ~/.claude-<slug> sibling), so the
        // config-folder row reads like the one a user would see.
        string personal = Path.Combine(root, ".claude");
        string work = Path.Combine(root, ".claude-work");
        WritePersonal(personal, nowUtc);
        WriteTeam(work, nowUtc);

        var found = new List<ClaudeInfo> { ClaudeAccount.Read(personal), ClaudeAccount.Read(work) };
        found[0].IsDefault = true;
        WriteHistory(found[0], nowUtc, week);
        return found;
    }

    /// <summary>
    /// A week of stored readings for the personal profile, which is the missing half of this fixture
    /// (T190): the System page's extra-usage row says whether the allowance is *in use* by reading the
    /// profile's own <c>usage-history.jsonl</c>, and with no store to read it always took the absent path.
    /// The branch that shows a percentage — "Enabled — in use now (42%)" — had therefore never been
    /// rendered, on a page whose whole reason for having a fixture is that it gets published.
    ///
    /// <para>The series is also the store's own <c>absent ≠ zero</c> rule (T179) made visible: it runs from
    /// readings carrying no overage figure at all, through one measured zero, to a figure that climbs. So a
    /// chart drawn from it shows a second series that starts where the measurements start, not a floor
    /// along the bottom of the week.</para>
    ///
    /// <para>Written by <see cref="UsageHistory.Append"/> itself, for the same reason the config dirs are
    /// read back through <see cref="ClaudeAccount.Read(string)"/>: a fixture that formats the lines itself
    /// is a second implementation of the format, free to drift from the one that matters. It lands under
    /// the fixture profile's own key — a hash of an invented account uuid — so no real profile's series is
    /// touched, and it is cleared first so a rebuild is a rewrite rather than a longer week.</para>
    /// </summary>
    private static void WriteHistory(ClaudeInfo personal, DateTime nowUtc, SampleWeek which)
    {
        string key = ProfileStore.KeyFor(personal);
        UsageHistory.Clear(key);
        // The absent branch is the *lack* of a store, so the clear above is the whole fixture for it.
        if (which == SampleWeek.Absent) return;

        long now = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToUnixTimeSeconds();
        long reset7d = now + 3 * 86400;         // 4 days of the week gone, 3 to go
        long reset5h = now + 2 * 3600;

        // A week that spent its included quota and kept working. `null` for the readings before the
        // account was ever past its limit, because that is what the API sent: no figure, not a zero.
        (double hoursAgo, double u7, double u5, double? extra)[] spending =
        {
            (96, 0.10, 0.20, null),
            (72, 0.28, 0.55, null),
            (48, 0.51, 0.30, null),
            (24, 0.78, 0.62, null),
            (8,  0.96, 0.44, 0.0),              // measured, and nothing spent past the quota yet
            (3,  1.00, 0.58, 0.18),             // the quota is gone; the overage clock starts
            (0,  1.00, 0.72, 0.42),             // what the row reports: in use now, at 42%
        };

        // A busy week that never ran out: the allowance is enabled and measured, and every figure is a
        // real zero rather than a missing one. That is the third branch — "Enabled — not in use" — and
        // the reason it needs its own week is that the one above cannot end in a zero without ceasing
        // to be the week T190 renders.
        (double hoursAgo, double u7, double u5, double? extra)[] zero =
        {
            (96, 0.09, 0.18, null),
            (72, 0.24, 0.51, null),
            (48, 0.44, 0.28, 0.0),
            (24, 0.61, 0.60, 0.0),
            (8,  0.74, 0.42, 0.0),
            (3,  0.81, 0.55, 0.0),
            (0,  0.86, 0.69, 0.0),              // what the row reports: enabled, and not in use
        };

        foreach ((double hoursAgo, double u7, double u5, double? extra) in
                 which == SampleWeek.Zero ? zero : spending)
            UsageHistory.Append(key, now - (long)(hoursAgo * 3600), u5, reset5h, u7, reset7d,
                                extra, extra is null ? 0 : reset7d);
    }

    /// <summary>A personal Max 20x subscription: no organization, so the org row collapses.</summary>
    private static void WritePersonal(string dir, DateTime nowUtc)
    {
        Config(dir, new
        {
            installMethod = "native",
            autoUpdates = true,
            claudeCodeFirstTokenDate = Iso(nowUtc.AddDays(-96)),
            projects = Projects(9),
            oauthAccount = new
            {
                accountUuid = "00000000-0000-4000-8000-000000000001",
                emailAddress = "ada.lovelace@example.com",
                displayName = "Ada Lovelace",
                userRateLimitTier = "default_claude_max_20x",
                billingType = "subscription",
                hasExtraUsageEnabled = true,
                accountCreatedAt = Iso(nowUtc.AddDays(-402)),
                subscriptionCreatedAt = Iso(nowUtc.AddDays(-118)),
            },
        });
        Credentials(dir, nowUtc, "max");
    }

    /// <summary>A Team seat: an organization, its type and the holder's role in it.</summary>
    private static void WriteTeam(string dir, DateTime nowUtc)
    {
        Config(dir, new
        {
            installMethod = "native",
            autoUpdates = true,
            claudeCodeFirstTokenDate = Iso(nowUtc.AddDays(-164)),
            projects = Projects(23),
            oauthAccount = new
            {
                accountUuid = "00000000-0000-4000-8000-000000000002",
                emailAddress = "grace.hopper@northwind.example",
                displayName = "Grace Hopper",
                userRateLimitTier = "default_claude_team",
                seatTier = "team_tier_1",
                billingType = "seat",
                hasExtraUsageEnabled = false,
                organizationUuid = "00000000-0000-4000-8000-0000000000a1",
                organizationName = "Northwind Robotics",
                organizationType = "team",
                organizationRole = "developer",
                accountCreatedAt = Iso(nowUtc.AddDays(-233)),
                subscriptionCreatedAt = Iso(nowUtc.AddDays(-180)),
            },
        });
        Credentials(dir, nowUtc, "team");
    }

    private static void Config(string dir, object config) =>
        Write(Path.Combine(dir, ".claude.json"), JsonSerializer.Serialize(config, Indented));

    /// <summary>
    /// The credentials file, minus the credentials: <see cref="ClaudeAccount"/> reads only the expiry,
    /// the subscription type and how many scopes were granted, so those are the only three fields the
    /// fixture writes. A stand-in token would be a secret-shaped string on disk for no benefit.
    /// </summary>
    private static void Credentials(string dir, DateTime nowUtc, string subscriptionType) =>
        Write(Path.Combine(dir, ".credentials.json"), JsonSerializer.Serialize(new
        {
            claudeAiOauth = new
            {
                expiresAt = new DateTimeOffset(nowUtc.AddHours(8), TimeSpan.Zero).ToUnixTimeMilliseconds(),
                scopes = new[] { "user:inference", "user:profile" },
                subscriptionType,
            },
        }, Indented));

    /// <summary>The <c>projects</c> map, which the page reads only for its size. The paths are invented
    /// and lead nowhere — nothing resolves them, and a fixture must not name a real repository.</summary>
    private static Dictionary<string, object> Projects(int count)
    {
        var map = new Dictionary<string, object>();
        for (int i = 1; i <= count; i++) map[$@"D:\work\sample-project-{i}"] = new { };
        return map;
    }

    private static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.000Z");

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
