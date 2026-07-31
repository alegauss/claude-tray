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
    /// Create the two config dirs and read them back the way the page does. The first stands in for the
    /// profile a bare <c>claude</c> would land in, so the picker shows its "(default)" marking.
    /// </summary>
    public static List<ClaudeInfo> Build(DateTime nowUtc)
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
        return found;
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
