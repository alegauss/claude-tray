namespace ClaudeTray;

/// <summary>Which of the three things reaching 100% means for this account (T182).</summary>
internal enum QuotaState
{
    /// <summary>Inside the quota included in the plan — the ordinary case, and every case before T182.</summary>
    InQuota,

    /// <summary>Past the included quota and still working, because extra usage pays for it. Not an error
    /// and not an alarm: a state the user chose, which costs money while it lasts.</summary>
    Billing,

    /// <summary>At the limit with nothing to spend past it — work is actually blocked until the reset.</summary>
    Stopped,
}

/// <summary>
/// The one place that decides which of the three states an account is in (T182).
///
/// <para>Before this, <c>AtLimitThreshold</c> drove the icon's red, the tooltip's "at limit" sentence and
/// the poll's idle, and all three encoded one binary: under the limit, or stopped. The third state — past
/// the quota and billing — was in the model all along, read by a single line of the Settings page, and
/// every other surface drew it as the third.</para>
///
/// <para>It lives here rather than on <c>TrayContext</c> because three surfaces answer the same question
/// and a state duplicated in three places is three places that will eventually disagree — and because a
/// tray cannot be constructed under <c>--selftest</c>, which is how the previous binary went unasserted.</para>
/// </summary>
internal static class QuotaStates
{
    /// <summary>At or above this utilization (0–1) the included quota is spent. Kept here so the poll's
    /// idle and the icon's verdict cannot drift onto different numbers.</summary>
    public const double AtLimitThreshold = 0.995;

    /// <summary>
    /// Whether the account can go on consuming past its included quota.
    ///
    /// <para>Two things say it can, and either is enough: <paramref name="extraUsageEnabled"/> is the
    /// account's own <c>hasExtraUsageEnabled</c>, and an <paramref name="extraUtil"/> above zero is the
    /// account demonstrably doing it whatever the flag says. Deliberately asymmetric — believing "can
    /// spend" wrongly costs one API call per interval, believing "stopped" wrongly loses readings across
    /// the exact stretch that cost money, and nothing recovers those (T180).</para>
    ///
    /// <para>A third thing says it can, and only one caller is allowed to believe it:
    /// <paramref name="extraStatus"/>, the overage window's own status header. It is the response *stating*
    /// what the two other signals infer — the flag is read out of a second file, and a utilization above
    /// zero is evidence after the fact. See <see cref="Allows"/> for why passing it is a caller's decision
    /// rather than this method's default.</para>
    ///
    /// <para>Note what is <em>not</em> decided here: whether the extra-usage allowance is itself spent.
    /// T181 established the window — its own utilization, resetting on a calendar month — and not the
    /// amount, so reading 1.0 as "stopped again" would still invent a denominator nothing has measured.</para>
    /// </summary>
    public static bool CanSpendPastQuota(double? extraUtil, bool? extraUsageEnabled, string? extraStatus = null)
        => extraUtil > 0 || extraUsageEnabled == true || Allows(extraStatus);

    /// <summary>
    /// Whether a window's status string is one of the permitted ones. The observed family is
    /// <c>allowed</c> and <c>allowed_warning</c>, so the prefix is the rule and every other value — an
    /// absent header, <c>unknown</c>, or a refusal whose spelling nobody here has seen — simply fails to
    /// affirm and leaves the older two signals to answer.
    ///
    /// <para><b>Why this can only ever add a "yes".</b> One reading of one account, inside its quota, sent
    /// <c>overage-status: allowed</c> — and that account has <c>hasExtraUsageEnabled</c> set, so the reading
    /// cannot distinguish "you are permitted" from a value every response carries regardless. Believing it
    /// wrongly must therefore cost only what the cheap direction costs. For the poll's idle that is one API
    /// call per interval on an account already past 100% (T180), which is the trade that block chose
    /// deliberately. For <see cref="Resolve"/> it would be a sentence on screen — a clay bar and "extra
    /// usage is paying" shown to somebody whose work has actually stopped — so the display keeps inferring
    /// until a reading exists that tells the two apart. The probe records the moment one arrives.</para>
    /// </summary>
    public static bool Allows(string? status)
        => status != null && status.StartsWith("allowed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this pair of consecutive readings is the moment the meter started (T184).
    ///
    /// <para>A <em>rise</em>, not a state: the previous reading must be a measured zero. <c>null</c> is
    /// not zero (T179) and deliberately does not arm it — a profile whose history predates that field, or
    /// a response carrying no overage header, has never been observed at zero, and announcing a "start"
    /// from a reading nobody took is how a notification loses its credibility. Equally, a previous reading
    /// already above zero is a spell in progress, not its beginning.</para>
    /// </summary>
    public static bool StartsSpending(double? previous, double? current)
        => current > 0 && previous is { } was && was <= 0;

    /// <summary>Which state a reading puts the account in. Deliberately does not take the overage status —
    /// what this returns becomes a colour and a sentence, and <see cref="Allows"/> says why an unmeasured
    /// affirmative may not reach either.</summary>
    public static QuotaState Resolve(double util, double? extraUtil, bool? extraUsageEnabled)
        => util < AtLimitThreshold ? QuotaState.InQuota
         : CanSpendPastQuota(extraUtil, extraUsageEnabled) ? QuotaState.Billing
         : QuotaState.Stopped;
}
