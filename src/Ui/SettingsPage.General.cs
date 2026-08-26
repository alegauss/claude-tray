using System.Windows;

namespace ClaudeTray;

/// <summary>Part of <see cref="SettingsPage"/> — one page per file, split out by T134, moved verbatim.</summary>
internal partial class SettingsPage
{
    // Approximate tokens billed per heartbeat: the request carries ~10 input tokens
    // ("hi" + message framing) and max_tokens=1 caps the reply at 1 output token.
    private const double InputTokensPerCall = 10;

    private const double OutputTokensPerCall = 1;

    // Haiku 4.5 price per 1M tokens (input, output) — matches the table in UsageInsights.
    private const double HaikuInputPerM = 1.0;

    private const double HaikuOutputPerM = 5.0;

    private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateIntervalLabel();

    private void RetrySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateRetryLabel();

    private void UpdateRetryLabel()
    {
        // RetryValue can be null while the slider's initial value is set during InitializeComponent.
        if (RetryValue is null) return;
        RetryValue.Text = RetryText((int)Math.Round(RetrySlider.Value));
    }

    // Through L.N rather than a hand-rolled `== 1 ?` (T376). This call site and the one below were two of
    // the three that had written the same conditional themselves — and this one passed the raw int, so its
    // number went through the current culture instead of Nums (T216) on the one surface where a comma for
    // a decimal point is the difference between 1.5 and 15.
    private static string RetryText(int seconds) => L.N("settings.sec", seconds);

    private void UpdateIntervalLabel()
    {
        // IntervalValue can be null while the slider's initial value is set during InitializeComponent.
        if (IntervalValue is null) return;
        double m = IntervalSlider.Value;
        IntervalValue.Text = IntervalText(m);
        UpdateCostEstimate(m);
    }

    // The number is invariant like every other one in the app (Nums, T216); only the unit is translated —
    // and L.N is now the one place that holds both halves of that (T376).
    private static string IntervalText(double minutes) => L.N("settings.min", minutes);

    // Show, live for the chosen cadence, how much the polling "heartbeat" consumes. It uses Claude
    // Code's subscription login, so there is no separate bill — it just draws a sliver of your usage;
    // the $ figure is only the hypothetical pay-as-you-go API equivalent, for a sense of scale.
    private void UpdateCostEstimate(double minutes)
    {
        if (CostEstimate is null) return;

        // One heartbeat per *polled* profile per interval (T127), so the figures multiply — and say so.
        // Only profiles on the subscription are polled: an API key has no quota window to read, and
        // polling it would spend money to learn nothing.
        int polled = Math.Max(1, DiscoverProfiles().Count(p => p.CountsAgainstSubscription));

        double callsPerHour = 60.0 / minutes * polled;
        double tokensPerCall = InputTokensPerCall + OutputTokensPerCall;
        double tokensPerHour = callsPerHour * tokensPerCall;
        double tokensPerDay = tokensPerHour * 24.0;
        double costPerCall = (InputTokensPerCall * HaikuInputPerM + OutputTokensPerCall * HaikuOutputPerM) / 1_000_000.0;
        double costPerMonth = costPerCall * callsPerHour * 24.0 * 30.0;

        CostEstimate.Text = CostText(callsPerHour, tokensPerHour, tokensPerDay, costPerMonth, polled);
    }

    // The heartbeat's four figures and the profile count under them, out of the render so the number
    // sweep can call it (T216). Every figure goes through `Nums`; the surrounding words are localized.
    private static string CostText(double callsPerHour, double tokensPerHour, double tokensPerDay,
                                   double costPerMonth, int polled) =>
        L.T("settings.cost.lead")
        + L.T("settings.cost.stats", Nums.Of(callsPerHour), Nums.Of(tokensPerHour, "0"),
              Nums.Of(tokensPerDay, "#,0"), Nums.Of(costPerMonth, "0.00"))
        + (polled > 1 ? "\n" + L.T("settings.cost.profiles", polled) : "");
}
