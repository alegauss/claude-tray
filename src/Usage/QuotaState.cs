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
    /// <para>Note what is <em>not</em> decided here: whether the extra-usage allowance is itself spent.
    /// Nothing has yet established what the overage percentage is a percentage of, so reading 1.0 as
    /// "stopped again" would invent the denominator T181 exists to go and measure.</para>
    /// </summary>
    public static bool CanSpendPastQuota(double? extraUtil, bool? extraUsageEnabled)
        => extraUtil > 0 || extraUsageEnabled == true;

    /// <summary>Which state a reading puts the account in.</summary>
    public static QuotaState Resolve(double util, double? extraUtil, bool? extraUsageEnabled)
        => util < AtLimitThreshold ? QuotaState.InQuota
         : CanSpendPastQuota(extraUtil, extraUsageEnabled) ? QuotaState.Billing
         : QuotaState.Stopped;
}
