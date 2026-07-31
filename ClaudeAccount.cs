using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ClaudeTray;

/// <summary>
/// One reading of the local Claude Code installation: which plan the signed-in account is on, who it
/// belongs to, where its configuration lives, and what the machine around it is. Every field is
/// nullable — a fresh install, an API-key setup (no OAuth account at all), Bedrock/Vertex, or a
/// half-written config all have to degrade to "unknown" rather than throw.
/// </summary>
internal sealed class ClaudeInfo
{
    // ---- Account (from .claude.json → oauthAccount, and .credentials.json) ----
    /// <summary>Plan as a person says it, e.g. "Claude Max 5x". Null when nothing identifies it.</summary>
    public string? Plan;
    /// <summary>The raw tier string behind <see cref="Plan"/>, shown as-is when it isn't one we map.</summary>
    public string? PlanTier;
    public string? SeatTier;
    public string? BillingType;
    public string? SubscriptionType;   // .credentials.json — coarser than PlanTier, but survives alone
    public string? HolderName;
    public string? HolderEmail;
    public string? OrgName;
    public string? OrgType;
    public string? OrgRole;
    public bool? ExtraUsage;
    public DateTime? AccountCreated;
    public DateTime? SubscriptionCreated;
    public DateTime? FirstToken;       // claudeCodeFirstTokenDate — "using Claude Code since"
    public DateTime? TokenExpires;     // the OAuth access token's own expiry
    public int ScopeCount;

    // ---- Installation ----
    public string ConfigDir = "";
    /// <summary>True when <c>CLAUDE_CONFIG_DIR</c> moved the config away from <c>~/.claude</c>.</summary>
    public bool ConfigDirOverridden;
    public string? CliVersion;
    public string? InstallMethod;
    public bool? AutoUpdates;
    public int ProjectCount;

    /// <summary>Whether anything at all identified a signed-in account. False → the page shows a
    /// "not signed in / API key" hint instead of a card full of dashes.</summary>
    public bool HasAccount => Plan != null || HolderEmail != null || SubscriptionType != null;
}

/// <summary>
/// Reads <c>.claude.json</c> and <c>.claude/.credentials.json</c> — the two files Claude Code already
/// keeps on disk — into a <see cref="ClaudeInfo"/>. Read-only by construction (§I.4): it opens files,
/// never writes one, and it touches no transcript, so the privacy promise (§I.1) is untouched — this
/// is configuration and account metadata, not conversation.
///
/// No secret ever leaves this class: the credentials file holds the access/refresh tokens and only its
/// <c>expiresAt</c>, <c>subscriptionType</c> and <c>scopes</c> length are read out of it.
/// </summary>
internal static class ClaudeAccount
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Where Claude Code keeps its configuration: <c>CLAUDE_CONFIG_DIR</c> when set,
    /// otherwise <c>~/.claude</c>. Every path below is derived from this.</summary>
    public static string ConfigDir
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            return string.IsNullOrWhiteSpace(env) ? Path.Combine(Home, ".claude") : env.Trim();
        }
    }

    public static ClaudeInfo Read()
    {
        var info = new ClaudeInfo
        {
            ConfigDir = ConfigDir,
            ConfigDirOverridden = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")),
            CliVersion = ReadCliVersion(),
        };

        ReadConfig(info);
        ReadCredentials(info);

        // The account's own tier is the precise answer; the credentials file's coarse subscriptionType
        // is the fallback for a config that has no oauthAccount block yet.
        info.Plan ??= FriendlyPlan(info.SubscriptionType);
        return info;
    }

    // ---- .claude.json ------------------------------------------------------------------------

    /// <summary>The settings file lives beside the config dir when <c>CLAUDE_CONFIG_DIR</c> is set,
    /// and at <c>~/.claude.json</c> otherwise; try both so either layout reads.</summary>
    private static IEnumerable<string> ConfigPaths()
    {
        yield return Path.Combine(ConfigDir, ".claude.json");
        yield return Path.Combine(Home, ".claude.json");
    }

    private static void ReadConfig(ClaudeInfo info)
    {
        foreach (string path in ConfigPaths())
        {
            JsonDocument? doc = TryParse(path);
            if (doc is null) continue;
            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;

                info.InstallMethod = Str(root, "installMethod");
                info.AutoUpdates = Bool(root, "autoUpdates");
                info.FirstToken = Iso(root, "claudeCodeFirstTokenDate");
                if (root.TryGetProperty("projects", out JsonElement projects)
                    && projects.ValueKind == JsonValueKind.Object)
                    info.ProjectCount = projects.EnumerateObject().Count();

                if (root.TryGetProperty("oauthAccount", out JsonElement acct)
                    && acct.ValueKind == JsonValueKind.Object)
                {
                    info.PlanTier = Str(acct, "userRateLimitTier");
                    info.Plan = FriendlyPlan(info.PlanTier);
                    info.SeatTier = Str(acct, "seatTier");
                    info.BillingType = Str(acct, "billingType");
                    info.HolderName = Str(acct, "displayName");
                    info.HolderEmail = Str(acct, "emailAddress");
                    info.OrgName = Str(acct, "organizationName");
                    info.OrgType = Str(acct, "organizationType");
                    info.OrgRole = Str(acct, "organizationRole") ?? Str(acct, "workspaceRole");
                    info.ExtraUsage = Bool(acct, "hasExtraUsageEnabled");
                    info.AccountCreated = Iso(acct, "accountCreatedAt");
                    info.SubscriptionCreated = Iso(acct, "subscriptionCreatedAt");
                }
                return;   // first readable config wins
            }
        }
    }

    // ---- .credentials.json -------------------------------------------------------------------

    private static void ReadCredentials(ClaudeInfo info)
    {
        JsonDocument? doc = TryParse(Path.Combine(ConfigDir, ".credentials.json"));
        if (doc is null) return;
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out JsonElement oauth)
                || oauth.ValueKind != JsonValueKind.Object) return;

            info.SubscriptionType = Str(oauth, "subscriptionType");
            if (oauth.TryGetProperty("expiresAt", out JsonElement exp)
                && exp.ValueKind == JsonValueKind.Number
                && exp.TryGetInt64(out long ms) && ms > 0)
                info.TokenExpires = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
            if (oauth.TryGetProperty("scopes", out JsonElement scopes)
                && scopes.ValueKind == JsonValueKind.Array)
                info.ScopeCount = scopes.GetArrayLength();
        }
    }

    // ---- CLI version -------------------------------------------------------------------------

    /// <summary>
    /// The newest CLI version in the native installer's own store
    /// (<c>~/.local/share/claude/versions/</c>, one entry per version — a file on Windows, since the
    /// entry *is* the executable, so both files and directories are considered). Deliberately not
    /// <c>claude --version</c>: spawning Node to render a settings label costs seconds, and a tray app
    /// has no business launching a process to fill in a row. An npm/global install keeps no such
    /// store, so this stays null there and the row reads "—".
    /// </summary>
    private static string? ReadCliVersion()
    {
        try
        {
            string dir = Path.Combine(Home, ".local", "share", "claude", "versions");
            if (!Directory.Exists(dir)) return null;

            string? best = null;
            Version? bestParsed = null;
            foreach (string entry in Directory.GetFileSystemEntries(dir))
            {
                // Tolerate an extension (…/2.1.220.exe) — the version is the name without it.
                string name = Path.GetFileName(entry);
                if (!Version.TryParse(name, out Version? v)
                    && !Version.TryParse(Path.GetFileNameWithoutExtension(name), out v)) continue;
                if (bestParsed is null || v > bestParsed) { bestParsed = v; best = v.ToString(); }
            }
            return best;
        }
        catch { return null; }   // unreadable install dir is not worth failing the page over
    }

    // ---- Presentation helpers ----------------------------------------------------------------

    /// <summary>
    /// A rate-limit tier as a person names their plan: <c>default_claude_max_5x</c> → "Claude Max 5x".
    /// An unmapped tier falls back to the prettified raw string rather than to nothing — a plan we
    /// haven't seen yet should still read, and be recognizable in a bug report.
    /// </summary>
    public static string? FriendlyPlan(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier)) return null;
        string t = tier.ToLowerInvariant();
        // Most specific first: "max_20x" also contains "max".
        if (t.Contains("max_20x") || t.Contains("max20x")) return "Claude Max 20x";
        if (t.Contains("max_5x") || t.Contains("max5x")) return "Claude Max 5x";
        if (t.Contains("enterprise")) return "Claude Enterprise";
        if (t.Contains("team")) return "Claude Team";
        if (t.Contains("max")) return "Claude Max";
        if (t.Contains("pro")) return "Claude Pro";
        if (t.Contains("free")) return "Claude Free";
        return Pretty(tier);
    }

    /// <summary>An API enum turned readable: <c>team_tier_1</c> → "Team tier 1".</summary>
    public static string Pretty(string raw)
    {
        string s = raw.Trim();
        if (s.StartsWith("default_", StringComparison.OrdinalIgnoreCase)) s = s["default_".Length..];
        s = s.Replace('_', ' ').Replace('-', ' ').Trim();
        if (s.Length == 0) return raw;
        return char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];
    }

    /// <summary>An email with its local part hidden but its shape kept, so the row still reads as an
    /// address in a screenshot without being one: <c>a•••••@example.com</c>.</summary>
    public static string MaskEmail(string email)
    {
        int at = email.IndexOf('@');
        if (at <= 0) return new string('•', Math.Max(email.Length, 3));
        return email[0] + new string('•', Math.Max(at - 1, 3)) + email[at..];
    }

    /// <summary>A display name reduced to initials — "Ada Lovelace" → "A. L.".</summary>
    public static string MaskName(string name)
    {
        var sb = new StringBuilder();
        foreach (string part in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            sb.Append(char.ToUpper(part[0], CultureInfo.InvariantCulture)).Append(". ");
        return sb.Length > 0 ? sb.ToString().TrimEnd() : new string('•', 6);
    }

    // ---- JSON plumbing -----------------------------------------------------------------------

    // Any unreadable or malformed file yields null: the caller shows "—" for whatever it carried.
    // Shared-read so a running Claude Code writing the file can't make the page fail.
    private static JsonDocument? TryParse(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return JsonDocument.Parse(fs);
        }
        catch { return null; }
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
        && v.GetString() is { Length: > 0 } s ? s : null;

    private static bool? Bool(JsonElement o, string name) =>
        o.TryGetProperty(name, out JsonElement v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

    private static DateTime? Iso(JsonElement o, string name) =>
        Str(o, name) is { } s && DateTime.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime d)
            ? d.ToLocalTime() : null;
}
