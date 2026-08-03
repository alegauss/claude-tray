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

    /// <summary>
    /// Why a capture of the System information page is refused without <c>--sample</c> (T205), as the
    /// message to print — or null when the capture may proceed.
    ///
    /// <para>The asymmetry is the whole rule: interactive <c>--settings System</c> <em>should</em> render
    /// the real login, because that is somebody looking at their own machine. Only the path that writes a
    /// <b>file</b> has no such excuse, and this fixture exists precisely because that file gets published.
    /// T197 stopped a real account naming itself on a synthetic chart and T200 stopped an unbuildable
    /// <c>--sample</c> falling back to the real one; both left the plainest path alone — no flag at all,
    /// which renders the true holder and organization correctly and marks the result as nothing.</para>
    ///
    /// <para>Narrow on purpose. It refuses one page on one flag, which breaks no caller that exists: no
    /// other reason to render a real account off-screen into a PNG has ever come up. The same shape is
    /// already precedent — <see cref="StatsPreviews"/> refuses a variant to <c>--capture-stats</c> that
    /// <c>--stats</c> is allowed to show.</para>
    /// </summary>
    internal static string? CaptureRefusal(string? page, bool sampled)
    {
        if (sampled || !string.Equals(page, "System", StringComparison.OrdinalIgnoreCase)) return null;
        return "--capture-settings System renders this machine's own Claude Code login, and a capture " +
               "is a file that gets published. Refusing rather than writing it: masking hides the " +
               "holder's name and the local part of the address, but the organization and the mail " +
               "domain ARE the reading. Add --sample for the fixture account (--reveal to unmask it), " +
               "or use --settings System to look at the real one on screen.";
    }

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
    /// <remarks><see cref="Refused"/> is T224's branch and the reason the enum grew a fourth: the row's
    /// value comes out of <c>.claude.json</c>, so "enabled locally, refused by the organisation" is a state
    /// no local file can be put into — and T205 refuses to capture the System page against a real login, so
    /// without a fixture the sentence naming why work stopped could never appear in a published picture.</remarks>
    internal enum SampleWeek { Spending, Zero, Absent, Refused }

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
        Console.WriteLine("  refused    enabled in the file, rejected by the API: 'Not available' + the reason");
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
        // Both stores are cleared for every week, not only the ones that write: a rebuild has to be able to
        // take the refusal *away* again, and a leftover probe log would keep the refused sentence on a page
        // built for another branch.
        HeaderProbe.Clear(key);
        // The absent branch is the *lack* of a store, so the clear above is the whole fixture for it.
        if (which == SampleWeek.Absent) return;

        long now = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToUnixTimeSeconds();
        if (which == SampleWeek.Refused) { WriteRefusal(key, now); return; }

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

    /// <summary>
    /// The refused account, as the API really answers it (T224). Copied from the reading T211 took on
    /// 2026-08-03 rather than invented: <c>overage-status: rejected</c>, <c>overage-disabled-reason:
    /// org_level_disabled</c>, and — the part that is easy to get wrong — <b>no overage utilization or
    /// reset header at all</b>, which is what a refused window sends. No usage history goes with it for the
    /// same reason: there is no allowance being spent to have a series of.
    ///
    /// <para>Written through <see cref="HeaderProbe.Record"/>, so the fixture holds no second copy of the
    /// line format — which also means <b>one</b> line lands however many times this is called: the probe
    /// writes on a change of shape, and a fixture that produced a line per call would be a fixture of a
    /// different store than the one shipping.</para>
    /// </summary>
    private static void WriteRefusal(string key, long now)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["anthropic-ratelimit-unified-5h-utilization"] = "1.0",
            ["anthropic-ratelimit-unified-5h-reset"] = (now + 2 * 3600).ToString(Nums.Fmt),
            ["anthropic-ratelimit-unified-5h-status"] = "rejected",
            ["anthropic-ratelimit-unified-7d-utilization"] = "0.72",
            ["anthropic-ratelimit-unified-7d-reset"] = (now + 3 * 86400).ToString(Nums.Fmt),
            ["anthropic-ratelimit-unified-7d-status"] = "allowed",
            ["anthropic-ratelimit-unified-overage-status"] = "rejected",
            ["anthropic-ratelimit-unified-overage-disabled-reason"] = "org_level_disabled",
        };
        HeaderProbe.Record(key, now, headers);
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

    /// <summary>
    /// The <c>projects</c> map, which the page reads only for its size. The paths are invented and lead
    /// nowhere — nothing resolves them, and a fixture must not name a real repository.
    ///
    /// <para>The first project is written <b>twice</b>, differing only in the case of the drive letter,
    /// because that is the shape a real file has (T225): Claude Code records whatever spelling the shell
    /// used, so one folder ends up under two keys. The map therefore has <c>count + 1</c> entries and still
    /// names <c>count</c> directories, which is exactly the gap the count has to close — and it keeps the
    /// published screenshot's number the same, since the number was never about keys.</para>
    /// </summary>
    private static Dictionary<string, object> Projects(int count)
    {
        var map = new Dictionary<string, object>();
        for (int i = 1; i <= count; i++) map[$@"D:\work\sample-project-{i}"] = new { };
        map[@"d:\work\sample-project-1"] = new { };
        return map;
    }

    private static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.000Z");

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
