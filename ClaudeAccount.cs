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
    // ---- Profile identity (which config dir this reading came from) ----
    /// <summary>How this profile reads in a picker: the organization name, else "Personal" for an
    /// account without one, else the config folder's name. Derived, never typed by the user.</summary>
    public string Label = "";
    /// <summary>The account's own uuid — the **identity** of a profile. Two config dirs can hold the
    /// same account (a copied config, a junction), and they must not be listed twice.</summary>
    public string? AccountUuid;
    /// <summary>True for the config dir Claude Code would use with no override — i.e. the profile a
    /// bare <c>claude</c> in a fresh shell lands in.</summary>
    public bool IsDefault;
    /// <summary>True when this dir is in the user's registered list, rather than merely discovered.
    /// Only a registered profile can be renamed or removed from the list.</summary>
    public bool IsRegistered;
    /// <summary>Working directory this profile opens in, from the tray's own settings ("" = the global
    /// default). Not read from Claude Code — the tray owns it.</summary>
    public string WorkDir = "";

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
    /// <summary>Whether this profile has an OAuth block in <c>.credentials.json</c> at all. False means
    /// "never signed in here" — the case where launching needs <c>claude auth login</c> rather than a
    /// bare <c>claude</c>. An *expired* token is not this: Claude Code refreshes that itself.</summary>
    public bool HasCredentialsFile;

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

    // ---- Which credentials the sessions in this profile actually use (T124) ----
    /// <summary>How Claude Code authenticates in this profile.</summary>
    public AuthMethod Auth = AuthMethod.None;
    /// <summary>Where that came from, named not valued — an env var's name, a settings file, or the
    /// CLI's own answer. A key's *value* is never read, kept or shown.</summary>
    public string? AuthSource;
    /// <summary>True when <see cref="Auth"/> was confirmed by <c>claude auth status</c> rather than
    /// inferred from files and the environment.</summary>
    public bool AuthConfirmed;

    /// <summary>
    /// Whether work done in this profile draws on the Claude subscription the tray measures. False for
    /// an API key, Bedrock or Vertex: that usage bills elsewhere and never touches the 5h/7d windows, so
    /// the tray's percentage does not describe what Claude Code is spending here.
    /// </summary>
    public bool CountsAgainstSubscription => Auth == AuthMethod.Subscription;
}

/// <summary>How a profile authenticates. Precedence is Claude Code's, measured: a completed OAuth login
/// in the config dir wins; without one, an environment key takes over.</summary>
internal enum AuthMethod
{
    /// <summary>Nothing to authenticate with — the profile has never been signed into.</summary>
    None,
    /// <summary>A claude.ai OAuth login: the subscription the tray's percentages are about.</summary>
    Subscription,
    /// <summary>An API key (env var, settings <c>env</c> block, or <c>apiKeyHelper</c>) — billed per use.</summary>
    ApiKey,
    /// <summary>Amazon Bedrock.</summary>
    Bedrock,
    /// <summary>Google Vertex AI.</summary>
    Vertex,
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

    /// <summary>The home config dir — <c>~/.claude</c>. The profile a bare <c>claude</c> uses when
    /// nothing overrides it.</summary>
    public static string HomeConfigDir => Path.Combine(Home, ".claude");

    /// <summary>Whether this process inherited a <c>CLAUDE_CONFIG_DIR</c>.</summary>
    private static string? EnvConfigDir
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
        }
    }

    /// <summary>Where Claude Code keeps its configuration for *this* process: <c>CLAUDE_CONFIG_DIR</c>
    /// when set, otherwise <c>~/.claude</c>.</summary>
    public static string ConfigDir => EnvConfigDir ?? HomeConfigDir;

    /// <summary>The reading for the config dir this process would use.</summary>
    public static ClaudeInfo Read() => Read(ConfigDir);

    /// <summary>
    /// The reading for one specific config dir — the seam the profile model needs. Anything absent
    /// stays null; nothing here throws on a missing, denied or half-written file.
    /// </summary>
    public static ClaudeInfo Read(string configDir)
    {
        var info = new ClaudeInfo
        {
            ConfigDir = configDir,
            ConfigDirOverridden = EnvConfigDir is { } env && SamePath(env, configDir),
            IsDefault = SamePath(configDir, ConfigDir),
            CliVersion = ReadCliVersion(),
        };

        ReadConfig(info);
        ReadCredentials(info);

        // The account's own tier is the precise answer; the credentials file's coarse subscriptionType
        // is the fallback for a config that has no oauthAccount block yet.
        info.Plan ??= FriendlyPlan(info.SubscriptionType);
        info.Label = DeriveLabel(info);
        ResolveAuth(info);
        return info;
    }

    // ---- .claude.json ------------------------------------------------------------------------

    /// <summary>
    /// Where a config dir's settings file lives. With <c>CLAUDE_CONFIG_DIR</c> it is *inside* the dir
    /// (verified: the CLI creates <c>&lt;dir&gt;/.claude.json</c> for a dir that didn't exist); the
    /// default layout instead keeps it at <c>~/.claude.json</c>, beside <c>~/.claude</c>. The home
    /// fallback is offered **only** for the home dir — falling back to it for another profile would
    /// read the default account's identity into that profile, which is the one wrong answer this
    /// method must never give.
    /// </summary>
    private static IEnumerable<string> ConfigPaths(string configDir)
    {
        yield return Path.Combine(configDir, ".claude.json");
        if (SamePath(configDir, HomeConfigDir)) yield return Path.Combine(Home, ".claude.json");
    }

    /// <summary>
    /// Fill <paramref name="info"/> from the profile's settings file — the **best** of the candidate
    /// paths rather than the first that parses, or the first that names an account.
    ///
    /// <para>For the home profile the authoritative file is <c>~/.claude.json</c>, but
    /// <c>~/.claude/.claude.json</c> can also exist — anything that ran with
    /// <c>CLAUDE_CONFIG_DIR=~/.claude</c> creates one, and this app used to do that itself (T136). It
    /// starts as a near-empty stub, which is why "prefer whichever names an account" was enough at
    /// first; then Claude Code fills in an <c>oauthAccount</c> on it, both candidates qualify, and the
    /// stub — first in candidate order, and carrying no <c>userRateLimitTier</c>, no organization and no
    /// projects — silently replaced the real reading. Observed live: a Max 5x / VILT Group / 39-project
    /// profile became "Claude Team", no org, 0 projects, and its derived label collapsed to "Personal",
    /// colliding with the second profile's.</para>
    ///
    /// <para>So candidates are **scored** on how much of the identity they actually carry. One rule
    /// overrides the score: a file naming a *different* account than the first candidate never wins,
    /// because the first candidate is the one that sits beside the <c>.credentials.json</c> this profile
    /// is polled with — if someone logged a second account into <c>~/.claude</c>, that login is what the
    /// dir *is*, and <c>~/.claude.json</c> is then the stale one.</para>
    /// </summary>
    private static void ReadConfig(ClaudeInfo info)
    {
        string? best = PickConfigFile(ConfigPaths(info.ConfigDir).ToList());
        if (best is null) return;
        foreach (string path in new[] { best })
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
                    info.AccountUuid = Str(acct, "accountUuid");
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

    /// <summary>
    /// Which candidate file describes this config dir best (see <see cref="ReadConfig"/> for why the
    /// first one is not the answer). Highest score wins, ties keep the earlier — and therefore
    /// closer-to-the-credentials — candidate; a candidate naming another account is skipped outright.
    /// </summary>
    private static string? PickConfigFile(List<string> paths)
    {
        string? bestPath = null, firstAccount = null;
        int bestScore = -1;
        foreach (string path in paths)
        {
            (int score, string? account) = ScoreConfig(path);
            if (score < 0) continue;                       // missing or unparseable
            firstAccount ??= account;
            // A different login is a different profile, not a better description of this one.
            if (account is { Length: > 0 } && firstAccount is { Length: > 0 }
                && !account.Equals(firstAccount, StringComparison.OrdinalIgnoreCase)) continue;
            if (score > bestScore) { bestScore = score; bestPath = path; }
        }
        return bestPath;
    }

    /// <summary>
    /// How much of a profile's identity a config file carries, and which account it names.
    /// <c>-1</c> means the file is missing or unreadable. The weights say what the UI would otherwise
    /// lose: an account block at all, then the rate-limit tier the plan is read from, then the
    /// organization the label is derived from, then any project history.
    /// </summary>
    private static (int Score, string? Account) ScoreConfig(string path)
    {
        JsonDocument? doc = TryParse(path);
        if (doc is null) return (-1, null);
        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (-1, null);
            if (!root.TryGetProperty("oauthAccount", out JsonElement a)
                || a.ValueKind != JsonValueKind.Object
                || !a.EnumerateObject().Any())
                return (0, null);                          // parses, but says nothing about who this is

            int score = 8;
            if (Str(a, "userRateLimitTier") is { Length: > 0 }) score += 4;
            if (Str(a, "organizationName") is { Length: > 0 }) score += 2;
            if (root.TryGetProperty("projects", out JsonElement projects)
                && projects.ValueKind == JsonValueKind.Object
                && projects.EnumerateObject().Any()) score += 1;
            return (score, Str(a, "accountUuid"));
        }
    }

    // ---- .credentials.json -------------------------------------------------------------------

    private static void ReadCredentials(ClaudeInfo info)
    {
        JsonDocument? doc = TryParse(Path.Combine(info.ConfigDir, ".credentials.json"));
        if (doc is null) return;
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out JsonElement oauth)
                || oauth.ValueKind != JsonValueKind.Object) return;

            info.HasCredentialsFile = true;
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

    // ---- Which credentials a profile actually uses (T124) ------------------------------------

    /// <summary>
    /// Work out how Claude Code would authenticate in a profile, from files and the environment only —
    /// no process spawn, so it costs nothing to show on every render. Precedence follows what Claude
    /// Code actually does (measured against a fresh config dir on a machine with a machine-level
    /// <c>ANTHROPIC_API_KEY</c>): a completed OAuth login in the config dir wins, and without one an
    /// environment key takes over. <see cref="QueryAuthStatusAsync"/> is the authoritative confirmation.
    ///
    /// <para>Only the *presence* of a key is ever read. No value is parsed, stored, logged or shown —
    /// including from a settings file, where a key would be sitting in plain text.</para>
    /// </summary>
    private static void ResolveAuth(ClaudeInfo info)
    {
        if (info.HasCredentialsFile)
        {
            info.Auth = AuthMethod.Subscription;
            info.AuthSource = "credentials.json";
            return;
        }

        // A third-party provider is chosen by env/settings and replaces Anthropic's own auth entirely.
        if (Flag(info, "CLAUDE_CODE_USE_BEDROCK", out string? src)) { info.Auth = AuthMethod.Bedrock; info.AuthSource = src; return; }
        if (Flag(info, "CLAUDE_CODE_USE_VERTEX", out src)) { info.Auth = AuthMethod.Vertex; info.AuthSource = src; return; }

        foreach (string name in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN" })
            if (Flag(info, name, out src)) { info.Auth = AuthMethod.ApiKey; info.AuthSource = src; return; }

        // apiKeyHelper is a settings-level way to supply a key without an env var.
        if (SettingsHas(info.ConfigDir, "apiKeyHelper")) { info.Auth = AuthMethod.ApiKey; info.AuthSource = "apiKeyHelper"; return; }

        info.Auth = AuthMethod.None;
        info.AuthSource = null;
    }

    /// <summary>Is this variable set for Claude Code in this profile — in the process environment (which
    /// already merges machine, user and process scopes) or in the profile's <c>settings.json</c>
    /// <c>env</c> block? Returns the *name* of where it came from, never a value.</summary>
    private static bool Flag(ClaudeInfo info, string variable, out string? source)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
        {
            source = variable;
            return true;
        }
        if (SettingsEnvHas(info.ConfigDir, variable))
        {
            source = $"settings.json · {variable}";
            return true;
        }
        source = null;
        return false;
    }

    private static bool SettingsEnvHas(string configDir, string key)
    {
        JsonDocument? doc = TryParse(Path.Combine(configDir, "settings.json"));
        if (doc is null) return false;
        using (doc)
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("env", out JsonElement env)
                   && env.ValueKind == JsonValueKind.Object
                   && env.TryGetProperty(key, out JsonElement v)
                   && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                   && (v.ValueKind != JsonValueKind.String || v.GetString() is { Length: > 0 });
    }

    private static bool SettingsHas(string configDir, string key)
    {
        JsonDocument? doc = TryParse(Path.Combine(configDir, "settings.json"));
        if (doc is null) return false;
        using (doc)
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(key, out JsonElement v)
                   && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    /// <summary>
    /// The CLI's own answer for a profile: <c>claude auth status --json</c> with that profile's
    /// <c>CLAUDE_CONFIG_DIR</c>. This is the authoritative reading — it resolves precedence the way the
    /// tool itself does, and it answers for setups no file read can see. It costs a Node start
    /// (seconds), so it belongs to an explicit "check" rather than to rendering a page.
    ///
    /// <para>The response carries no secret: <c>loggedIn</c>, <c>authMethod</c>, <c>apiProvider</c>,
    /// <c>apiKeySource</c> (a name, not a key), the account's org and subscription type.</para>
    /// </summary>
    public static async Task<(AuthMethod Auth, string? Source, string? Error)> QueryAuthStatusAsync(string configDir)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                // Through cmd.exe so PATHEXT and the installer's shim resolve `claude` the same way a
                // terminal would, whatever the install method.
                FileName = "cmd.exe",
                Arguments = "/c claude auth status --json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (NeedsConfigDirOverride(configDir))
                psi.Environment["CLAUDE_CONFIG_DIR"] = configDir;

            using System.Diagnostics.Process? proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return (AuthMethod.None, null, "could not start claude");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string json = await proc.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            bool loggedIn = root.TryGetProperty("loggedIn", out JsonElement li) && li.ValueKind == JsonValueKind.True;
            string? method = Str(root, "authMethod");
            string? provider = Str(root, "apiProvider");
            string? keySource = Str(root, "apiKeySource");

            AuthMethod auth = (method, provider) switch
            {
                (not null, _) when method!.Contains("claude.ai", StringComparison.OrdinalIgnoreCase) => AuthMethod.Subscription,
                (not null, _) when method!.Contains("api_key", StringComparison.OrdinalIgnoreCase)
                                   || method!.Contains("token", StringComparison.OrdinalIgnoreCase) => AuthMethod.ApiKey,
                (_, not null) when provider!.Contains("bedrock", StringComparison.OrdinalIgnoreCase) => AuthMethod.Bedrock,
                (_, not null) when provider!.Contains("vertex", StringComparison.OrdinalIgnoreCase) => AuthMethod.Vertex,
                _ => loggedIn ? AuthMethod.ApiKey : AuthMethod.None,
            };
            // `apiKeySource` is reported even when the method in use is claude.ai (it names a key that
            // merely *exists* in the environment), so it is only the source when a key is what is
            // actually authenticating. Otherwise the method itself is the honest source.
            return (auth, auth == AuthMethod.ApiKey ? keySource ?? method : method ?? provider, null);
        }
        catch (Exception e)
        {
            // A missing CLI, a timeout, a non-JSON answer: the inferred reading stands and says so.
            return (AuthMethod.None, null, e is OperationCanceledException ? "timeout" : e.Message);
        }
    }

    // ---- Profile discovery -------------------------------------------------------------------

    /// <summary>Directory name prefix a profile created by convention uses: <c>~/.claude-&lt;slug&gt;</c>.
    /// Discovery globs it, so a profile made by hand — or on another machine, in a synced home —
    /// is found without being registered anywhere.</summary>
    public const string ConventionPrefix = ".claude-";

    /// <summary>A project path is only probed for a settings override when it is local. A dead UNC
    /// path can block a stat for seconds, and this runs while a window is being built.</summary>
    private const int MaxProjectsProbed = 200;

    /// <summary>
    /// Every Claude Code profile on this machine, default first. A profile is a **config dir**;
    /// its identity is the account inside it.
    ///
    /// <para>Sources, in order and with no guessing: the dir this process would use, <c>~/.claude</c>,
    /// an <c>env.CLAUDE_CONFIG_DIR</c> in that dir's <c>settings.json</c>, the same key in each
    /// registered project's <c>.claude/settings.json</c> / <c>settings.local.json</c>, the
    /// <c>~/.claude-*</c> convention, and <paramref name="registered"/> — the caller's own list.</para>
    ///
    /// <para>A candidate counts as a profile when it holds <c>.credentials.json</c>, or a
    /// <c>.claude.json</c> naming an account, or when it was explicitly registered (the clause that
    /// covers a profile which exists but has never been logged into). Duplicates are dropped by path
    /// and then by <see cref="ClaudeInfo.AccountUuid"/>, so one account reached through two paths is
    /// listed once — under the first path, which is why the default is enumerated first.</para>
    /// </summary>
    /// <summary>
    /// Discovery over the tray's own registered list: the same sweep, with each entry's chosen label
    /// and working directory applied on top of what the files say. A registered dir is always listed —
    /// including one that has never been logged into, which is the whole point of registering it.
    /// </summary>
    public static List<ClaudeInfo> Discover(IEnumerable<ClaudeProfile>? profiles)
    {
        List<ClaudeProfile> list = profiles?.Where(p => !string.IsNullOrWhiteSpace(p.ConfigDir)).ToList()
                                  ?? new List<ClaudeProfile>();
        List<ClaudeInfo> found = Discover(list.Select(p => p.ConfigDir));

        foreach (ClaudeInfo info in found)
        {
            ClaudeProfile? reg = list.FirstOrDefault(p => SamePath(p.ConfigDir, info.ConfigDir));
            if (reg is null) continue;
            info.IsRegistered = true;
            info.WorkDir = reg.WorkDir;
            // A name the user typed wins over the derived one — that is what renaming means.
            if (!string.IsNullOrWhiteSpace(reg.Label)) info.Label = reg.Label.Trim();
        }
        return found;
    }

    /// <summary>
    /// Which profile the tray icon follows: the user's choice while that config dir is still on the
    /// machine, else the default profile, else the first found. One implementation, so the tray and
    /// <c>--profiles</c> can never disagree about whose number is on the icon.
    /// </summary>
    public static ClaudeInfo? PickMonitored(List<ClaudeInfo> profiles, string? monitoredConfigDir)
    {
        if (profiles.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(monitoredConfigDir))
            foreach (ClaudeInfo p in profiles)
                if (SamePath(p.ConfigDir, monitoredConfigDir)) return p;
        return profiles.FirstOrDefault(p => p.IsDefault) ?? profiles[0];
    }

    public static List<ClaudeInfo> Discover(IEnumerable<string>? registered = null)
    {
        var regs = new List<string>();
        foreach (string r in registered ?? Enumerable.Empty<string>())
            if (Normalize(r) is { Length: > 0 } n) regs.Add(n);

        var found = new List<ClaudeInfo>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string dir in Candidates(regs))
        {
            string norm = Normalize(dir);
            if (norm.Length == 0 || !seenPaths.Add(norm)) continue;

            bool isRegistered = regs.Contains(norm, StringComparer.OrdinalIgnoreCase);
            if (!isRegistered && !LooksLikeProfile(norm)) continue;

            ClaudeInfo info = Read(norm);
            // Same account through a second path (copied config, junction): keep the first.
            if (info.AccountUuid is { Length: > 0 } uuid && !seenAccounts.Add(uuid)) continue;
            if (!isRegistered && !info.HasAccount && !HasCredentials(norm)) continue;
            found.Add(info);
        }
        return found;
    }

    // The candidate dirs, in priority order. Yielding duplicates is fine — Discover dedupes.
    private static IEnumerable<string> Candidates(List<string> registered)
    {
        yield return ConfigDir;          // what this process would use (env override, if any)
        yield return HomeConfigDir;      // ~/.claude, which still exists when an override is set

        foreach (string d in SettingsOverride(Path.Combine(ConfigDir, "settings.json"))) yield return d;
        foreach (string d in SettingsOverride(Path.Combine(HomeConfigDir, "settings.json"))) yield return d;

        foreach (string project in ProjectPaths())
        {
            string dot = Path.Combine(project, ".claude");
            foreach (string d in SettingsOverride(Path.Combine(dot, "settings.json"))) yield return d;
            foreach (string d in SettingsOverride(Path.Combine(dot, "settings.local.json"))) yield return d;
        }

        foreach (string d in ConventionDirs()) yield return d;
        foreach (string d in registered) yield return d;
    }

    /// <summary><c>env.CLAUDE_CONFIG_DIR</c> out of a settings file, expanded. Zero or one value.</summary>
    private static IEnumerable<string> SettingsOverride(string settingsPath)
    {
        JsonDocument? doc = TryParse(settingsPath);
        if (doc is null) yield break;
        string? dir;
        using (doc)
        {
            dir = doc.RootElement.ValueKind == JsonValueKind.Object
                  && doc.RootElement.TryGetProperty("env", out JsonElement env)
                  && env.ValueKind == JsonValueKind.Object
                ? Str(env, "CLAUDE_CONFIG_DIR")
                : null;
        }
        if (dir is { Length: > 0 }) yield return Expand(dir);
    }

    /// <summary>The project directories Claude Code has registered, from the default config's
    /// <c>projects</c> map — cheaper and more exact than walking the disk. UNC paths are skipped
    /// (a dead share can block a stat for seconds) and the list is capped.</summary>
    private static IEnumerable<string> ProjectPaths()
    {
        foreach (string path in ConfigPaths(ConfigDir))
        {
            JsonDocument? doc = TryParse(path);
            if (doc is null) continue;
            var paths = new List<string>();
            using (doc)
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("projects", out JsonElement projects)
                    && projects.ValueKind == JsonValueKind.Object)
                    foreach (JsonProperty p in projects.EnumerateObject())
                    {
                        if (p.Name.StartsWith(@"\\") || p.Name.StartsWith("//")) continue;
                        paths.Add(p.Name);
                        if (paths.Count >= MaxProjectsProbed) break;
                    }
            }
            return paths;
        }
        return Enumerable.Empty<string>();
    }

    /// <summary>Sibling dirs of <c>~/.claude</c> following the <c>~/.claude-&lt;slug&gt;</c> convention.
    /// Directories only — <c>.claude.json</c> and <c>.claude.json.backup</c> also match the pattern.</summary>
    private static IEnumerable<string> ConventionDirs()
    {
        string[] dirs;
        try { dirs = Directory.GetDirectories(Home, ConventionPrefix + "*"); }
        catch { return Enumerable.Empty<string>(); }
        Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
        return dirs;
    }

    private static bool LooksLikeProfile(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return false;
            if (HasCredentials(dir)) return true;
            foreach (string path in ConfigPaths(dir))
                if (File.Exists(path)) return true;
            return false;
        }
        catch { return false; }   // denied or gone — not a profile we can read
    }

    private static bool HasCredentials(string dir)
    {
        try { return File.Exists(Path.Combine(dir, ".credentials.json")); }
        catch { return false; }
    }

    /// <summary>How a profile reads in a picker. Never asks the user: the organization when there is
    /// one, "Personal" for an account without one, and the folder name when nothing identifies it.</summary>
    private static string DeriveLabel(ClaudeInfo info)
    {
        if (info.OrgName is { Length: > 0 } org) return org;
        if (info.HasAccount) return L.T("settings.sys.profilePersonal");
        string name = Path.GetFileName(info.ConfigDir.TrimEnd('\\', '/'));
        return name.Length > 0 ? name : info.ConfigDir;
    }

    /// <summary>Expand <c>%VARS%</c> and a leading <c>~</c> in a path read from a settings file.</summary>
    private static string Expand(string path)
    {
        string p = Environment.ExpandEnvironmentVariables(path.Trim());
        if (p.StartsWith('~')) p = Path.Combine(Home, p[1..].TrimStart('/', '\\'));
        return p;
    }

    private static string Normalize(string dir)
    {
        try { return Path.GetFullPath(Expand(dir)).TrimEnd('\\', '/'); }
        catch { return ""; }
    }

    /// <summary>
    /// Whether running Claude Code for <paramref name="configDir"/> needs <c>CLAUDE_CONFIG_DIR</c> set
    /// at all — false for the dir a bare <c>claude</c> already uses.
    ///
    /// <para>Setting it to the default dir is not a harmless no-op: Claude Code then writes a second,
    /// near-empty <c>&lt;dir&gt;\.claude.json</c> beside the real <c>~/.claude.json</c> (measured), so the
    /// session runs against a state file with none of the user's project history — and the stub goes on
    /// to describe that profile to this app worse than the real file does (T136). Both the launch path
    /// and <see cref="QueryAuthStatusAsync"/> ask through here, so they cannot drift.</para>
    /// </summary>
    public static bool NeedsConfigDirOverride(string configDir) =>
        configDir is { Length: > 0 }
        && !SamePath(configDir, HomeConfigDir)
        && !SamePath(configDir, ConfigDir);

    /// <summary>Whether two config dirs are the same folder, spelled either way (a trailing slash, a
    /// relative form, <c>%USERPROFILE%</c>). The one comparison anything asking "is this the monitored
    /// profile?" should use, so a settings file written by hand can't split one profile into two.</summary>
    public static bool SamePath(string a, string b) =>
        Normalize(a).Equals(Normalize(b), StringComparison.OrdinalIgnoreCase);

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
