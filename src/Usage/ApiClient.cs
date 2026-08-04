using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ClaudeTray;

/// <summary>Usage snapshot read from the Anthropic rate-limit response headers.</summary>
internal sealed class UsageData
{
    public double Session5h;
    public double Week7d;
    public double Extra;
    public bool HasExtra;      // the overage header was on the response at all — see ExtraUtil
    public double Reset5h;     // unix seconds
    public double Reset7d;
    public double ResetExtra;
    public string Status = "unknown";

    /// <summary>The other two windows' status strings, which T181's reading found are sent exactly like
    /// 5h's: every window sends a <c>-utilization</c>, a <c>-reset</c> and a <c>-status</c>. Kept because
    /// the overage one is the only signal on the response that could state — rather than let this app infer
    /// from two places — whether an account may keep working past its included quota (T208).</summary>
    public string Status7d = "unknown";
    public string StatusExtra = "unknown";

    /// <summary>Why the overage window was refused, verbatim — <c>org_level_disabled</c> is the one value
    /// measured so far, and it arrives only on an account whose <c>overage-status</c> is <c>rejected</c>
    /// (T211's second account). Null when the header was not sent, which is every account that was not
    /// turned down. It is the only signal here that could say <em>why</em> work stopped rather than that it
    /// did, and <see cref="QuotaStates.Refuses"/> is what reads it (T224).</summary>
    public string? ExtraDisabledReason;

    /// <summary>The API stating that this account is spending past its included quota, right now —
    /// <c>overage-in-use</c>, and the only signal here that is neither read out of a file nor inferred from
    /// an effect (T273). <c>null</c> is the header not being sent, which is the measurement rather than an
    /// absence to shrug at: it was absent on nine consecutive readings inside the quota — one of them at
    /// 0.91, eighteen minutes before — and arrived as <c>true</c> on the first reading past it. That is the
    /// one property <see cref="QuotaStates.Allows"/> needed of <c>overage-status</c> and could not find,
    /// which is why this affirmative may paint a screen and that one may not.</summary>
    public bool? ExtraInUse;

    /// <summary>The threshold the 5h window has crossed, named by the API rather than chosen here —
    /// <c>5h-surpassed-threshold</c> (T281). <c>null</c> is the header not being sent, which is the common
    /// case and a reading in itself: it was absent on the four readings inside the quota, arrived as
    /// <c>0.9</c> on the one whose status first said <c>allowed_warning</c> at a utilization of 0.91, and
    /// read <c>1.0</c> on the one that said <c>rejected</c> at 1.02.
    ///
    /// <para>Two values in one log, each beside a status transition this app already reads — so what it
    /// announces is measured rather than inferred, which is what separates it from the single-sample
    /// vocabularies §XVIII.9 declines to map. <see cref="Settings.FlashWarnThreshold"/> is the number this
    /// repository picked for the same question, and <see cref="QuotaStates.Warns"/> is what decides between
    /// them.</para></summary>
    public double? Surpassed5h;

    /// <summary>Every <c>anthropic-ratelimit-*</c> response header, verbatim, exactly as the API sent it
    /// (T181). The parsed fields above are this app's *reading* of four of them; this is the reading
    /// nobody has made yet — what the overage percentage denominates, and what the status header says
    /// while an account is consuming overage. Quota metadata only: no message content and no credential
    /// ever appears in a response header, so §I.1 is untouched.</summary>
    public Dictionary<string, string> RateHeaders = new(StringComparer.OrdinalIgnoreCase);

    public string? Error;
    public bool Unauthorized;   // true on HTTP 401 / no usable token — Claude Code must re-auth
    public bool NeedsFullLogin; // when Unauthorized: true = no refresh token on disk, a full OAuth2
                                // browser /login is required; false = a refresh token exists, so just
                                // opening Claude Code silently refreshes the access token (no browser)
    public bool Transient;      // true when Error is a timeout/network/5xx blip worth retrying quietly

    /// <summary>The overage utilization as a reading rather than a number: null when the account has no
    /// overage header at all, <c>0</c> when it has one and nothing has been spent past the included
    /// quota. The two are different facts and only this form keeps them apart — <see cref="Extra"/> is
    /// 0 for both, which is fine for a tooltip that only draws above zero and wrong for anything that
    /// stores, charts or notifies on the figure (T179).</summary>
    public double? ExtraUtil => HasExtra ? Extra : null;

    public double Metric(string key) => key switch
    {
        "7d" => Week7d,
        "extra" => Extra,
        _ => Session5h,
    };

    public double ResetOf(string key) => key switch
    {
        "7d" => Reset7d,
        "extra" => ResetExtra,
        _ => Reset5h,
    };

    public string StatusOf(string key) => key switch
    {
        "7d" => Status7d,
        "extra" => StatusExtra,
        _ => Status,
    };
}

/// <summary>
/// Fetches usage via one minimal Anthropic API call (Haiku, 1 token) using the OAuth
/// token Claude Code stores in ~/.claude/.credentials.json, and reads the official
/// anthropic-ratelimit-unified-* headers from the response.
/// </summary>
internal sealed class ApiClient
{
    /// <summary>
    /// The profile this client reads its token from — a Claude Code config dir. Defaults to the one
    /// this process would use, which is the only one before T127 polls several; a rate limit belongs to
    /// an account, so reading two accounts' usage means two clients, each with its own token.
    /// </summary>
    private readonly string _credsPath;

    public ApiClient(string? configDir = null) =>
        _credsPath = Path.Combine(configDir ?? ClaudeAccount.ConfigDir, ".credentials.json");

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<UsageData> FetchAsync()
    {
        try
        {
            Creds creds = ReadCredentials();

            var body = new
            {
                model = "claude-haiku-4-5-20251001",
                max_tokens = 1,
                messages = new[] { new { role = "user", content = "hi" } },
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            using HttpResponseMessage resp = await _http.SendAsync(req).ConfigureAwait(false);
            string bodyText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            UsageData d = ReadHeaders(name => S(resp, name));
            // The whole family, kept verbatim rather than parsed: a header this app does not yet read is
            // exactly the one T181 needs to see, and it costs nothing — the response is already in hand.
            foreach (var h in resp.Headers)
                if (h.Key.StartsWith("anthropic-ratelimit-", StringComparison.OrdinalIgnoreCase))
                    d.RateHeaders[h.Key] = string.Join(", ", h.Value);

            // The unified rate-limit headers are the entire reading we care about. When none of them
            // come back — the call failed (expired token, 403…), or it "succeeded" but a gateway/edge
            // stripped the headers — there is no real usage data. Surface it as an error so the tray
            // keeps its last good value. This guard is critical: without it a header-less HTTP 200
            // parses as utilization 0, which the burn tracker reads as usage collapsing to 0% — firing
            // a phantom early "weekly reset" and wiping the burn history — when nothing actually reset.
            bool hasHeaders = resp.Headers.Contains("anthropic-ratelimit-unified-5h-utilization")
                              || resp.Headers.Contains("anthropic-ratelimit-unified-7d-utilization");
            if (!hasHeaders)
            {
                int code = (int)resp.StatusCode;
                // A 2xx that arrived without the headers is a transient blip worth retrying quietly,
                // same as a 5xx/429.
                d.Transient = resp.IsSuccessStatusCode || code >= 500 || code == 429;
                // Prefer the API's own explanation from the response body (e.g. a 403 "subscription
                // payment is past due" message) — it's far more actionable than a bare "HTTP 403".
                d.Error = ExtractApiError(bodyText)
                    ?? (d.Transient ? L.T("api.tempUnavailable", code) : $"HTTP {code}");
            }
            d.Unauthorized = (int)resp.StatusCode == 401;
            // A 401 with a refresh token on disk just needs a silent refresh (open Claude Code); without
            // one it needs a full OAuth2 browser login. Surface which, so the tray's message is targeted.
            if (d.Unauthorized)
            {
                d.NeedsFullLogin = !creds.HasRefreshToken;
                d.Error = creds.HasRefreshToken ? RefreshHint : SignInHint;
            }
            return d;
        }
        catch (NotAuthenticatedException e)
        {
            // No usable token on disk yet (file missing, or no claudeAiOauth.accessToken). Treat it
            // like an HTTP 401 so the tray shows the "needs auth" state and the faster retry/auto-open
            // kicks in — but with a message that says to sign in, not a raw file-not-found error.
            return new UsageData { Error = e.Message, Unauthorized = true, NeedsFullLogin = true };
        }
        catch (Exception e)
        {
            return new UsageData { Error = Friendly(e), Transient = IsTransient(e) };
        }
    }

    /// <summary>
    /// The whole of this app's <em>reading</em> of a response: every rate-limit header name that reaches a
    /// field, in one place, expressed against a lookup rather than against an <see cref="HttpResponseMessage"/>.
    ///
    /// <para><b>Why a lookup (T278).</b> A name is read here or it is read nowhere, and the read-out that
    /// says which is which has to come from this method — a table of names written beside it is a second
    /// thing to forget, and forgetting it is how a header can be marked read for months after the line that
    /// read it was deleted. Handing the parse a <c>get</c> makes the same code answer both questions: with a
    /// real response it parses, and with a recording lookup that answers nothing it enumerates itself.
    /// See <see cref="NamesRead"/>.</para>
    /// </summary>
    private static UsageData ReadHeaders(Func<string, string?> get)
    {
        var d = new UsageData
        {
            Session5h = N(get, "anthropic-ratelimit-unified-5h-utilization"),
            Week7d    = N(get, "anthropic-ratelimit-unified-7d-utilization"),
            Extra     = N(get, "anthropic-ratelimit-unified-overage-utilization"),
            Reset5h   = N(get, "anthropic-ratelimit-unified-5h-reset"),
            Reset7d   = N(get, "anthropic-ratelimit-unified-7d-reset"),
            ResetExtra = N(get, "anthropic-ratelimit-unified-overage-reset"),
            Status    = get("anthropic-ratelimit-unified-5h-status") ?? "unknown",
            Status7d  = get("anthropic-ratelimit-unified-7d-status") ?? "unknown",
            StatusExtra = get("anthropic-ratelimit-unified-overage-status") ?? "unknown",
            ExtraDisabledReason = get("anthropic-ratelimit-unified-overage-disabled-reason"),
            ExtraInUse = B(get, "anthropic-ratelimit-unified-overage-in-use"),
            Surpassed5h = NOrNull(get, "anthropic-ratelimit-unified-5h-surpassed-threshold"),
        };
        // Read the presence separately from the value: an account without extra usage sends no overage
        // header, and one that has it enabled but has spent nothing sends 0. N() collapses both to 0.0, so
        // the distinction has to be captured here or it cannot be recovered later.
        d.HasExtra = get("anthropic-ratelimit-unified-overage-utilization") != null;
        return d;
    }

    /// <summary>Every rate-limit header name <see cref="ReadHeaders"/> reads, derived by running it against a
    /// lookup that records what it was asked for and answers nothing (T278).
    ///
    /// <para>So it is the parser's own list, not a copy of it: a name this app stops reading leaves here the
    /// moment its line goes, and a name it starts reading arrives with no second edit. Everything else on a
    /// response is recorded verbatim and read by nothing — which <c>--probe</c> now says out loud, per
    /// header, rather than leaving it to be established by grepping this file.</para></summary>
    internal static IReadOnlyList<string> NamesRead { get; } = EnumerateNamesRead();

    private static IReadOnlyList<string> EnumerateNamesRead()
    {
        var seen = new List<string>();
        ReadHeaders(name =>
        {
            if (!seen.Contains(name, StringComparer.OrdinalIgnoreCase)) seen.Add(name);
            return null;
        });
        return seen;
    }

    // Timeouts (HttpClient throws TaskCanceledException), DNS/connection failures and similar
    // network blips are worth swallowing for a poll or two before alarming the user.
    private static bool IsTransient(Exception e) =>
        e is TaskCanceledException or OperationCanceledException or HttpRequestException
        || e.InnerException is HttpRequestException or System.Net.Sockets.SocketException;

    // Replace raw exception text (e.g. "The request was canceled due to the configured
    // HttpClient.Timeout of 30 seconds elapsing") with something a non-developer can read.
    private static string Friendly(Exception e) => e switch
    {
        TaskCanceledException or OperationCanceledException => L.T("api.timeout"),
        HttpRequestException => L.T("api.networkError"),
        _ => e.Message,
    };

    // Shown verbatim in the tray tooltip / Insights for each auth state.
    private static string SignInHint => L.T("api.signInHint");
    private static string RefreshHint => L.T("api.refreshHint");

    /// <summary>The OAuth material the tray needs: the bearer token, and whether a refresh token
    /// is on disk (which decides silent-refresh vs. full browser login on a 401).</summary>
    private readonly record struct Creds(string AccessToken, bool HasRefreshToken);

    private Creds ReadCredentials()
    {
        if (!File.Exists(_credsPath))
            throw new NotAuthenticatedException(SignInHint);

        using var doc = JsonDocument.Parse(File.ReadAllText(_credsPath));
        if (doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
            && oauth.TryGetProperty("accessToken", out var tok)
            && tok.GetString() is { Length: > 0 } token)
        {
            bool hasRefresh = oauth.TryGetProperty("refreshToken", out var rt)
                              && rt.GetString() is { Length: > 0 };
            return new Creds(token, hasRefresh);
        }

        throw new NotAuthenticatedException(SignInHint);
    }

    /// <summary>No usable OAuth token on disk yet — surfaced as a "needs sign-in" state, not an error.</summary>
    private sealed class NotAuthenticatedException(string message) : Exception(message);

    /// <summary>Pull the human-readable message out of an Anthropic error body
    /// (<c>{"type":"error","error":{"type":"...","message":"..."}}</c>), or null if the body isn't
    /// a recognizable error JSON.</summary>
    private static string? ExtractApiError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err)
                && err.TryGetProperty("message", out var msg)
                && msg.GetString() is { Length: > 0 } text)
                return text.Trim();
        }
        catch (JsonException) { /* not JSON — fall back to the HTTP status */ }
        return null;
    }

    private static double N(Func<string, string?> get, string name)
    {
        string? v = get(name);
        return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d : 0.0;
    }

    /// <summary>A figure whose <em>absence</em> is a reading, so it may not collapse to <c>N</c>'s 0.0 —
    /// a threshold nobody was told about is not a threshold of zero, which every reading would then be
    /// past (T281).</summary>
    private static double? NOrNull(Func<string, string?> get, string name)
        => get(name) is { } v
           && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;

    /// <summary>A header whose value is a word for true or false, read as three states: the header was not
    /// sent, it said so, or it said otherwise. <c>N</c>'s collapse of an absent header to 0.0 is exactly what
    /// must not happen here — absence is what the nine readings inside the quota measured (T273).</summary>
    private static bool? B(Func<string, string?> get, string name)
        => get(name) is { } v && bool.TryParse(v.Trim(), out bool b) ? b : null;

    private static string? S(HttpResponseMessage r, string name)
        => r.Headers.TryGetValues(name, out var vals) ? vals.FirstOrDefault() : null;
}
