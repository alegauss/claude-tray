using System.Windows;

namespace ClaudeTray;

/// <summary>Part of <see cref="SettingsWindow"/> — one page per file, split out by T134, moved verbatim.</summary>
internal partial class SettingsWindow : Window
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
        int s = (int)Math.Round(RetrySlider.Value);
        RetryValue.Text = s == 1 ? L.T("settings.sec.one") : L.T("settings.sec.many", s);
    }

    private void UpdateIntervalLabel()
    {
        // IntervalValue can be null while the slider's initial value is set during InitializeComponent.
        if (IntervalValue is null) return;
        double m = IntervalSlider.Value;
        // Keep the number invariant (period decimal) to match the rest of the app; translate only the unit.
        IntervalValue.Text = m == 1.0
            ? L.T("settings.min.one")
            : L.T("settings.min.many", m.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        UpdateCostEstimate(m);
    }

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

        // Format the numbers with the invariant culture so they read consistently (period decimal,
        // comma thousands) regardless of the OS locale; the surrounding words are localized.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string stats = L.T("settings.cost.stats",
            callsPerHour.ToString("0.#", inv),
            tokensPerHour.ToString("0", inv),
            tokensPerDay.ToString("#,0", inv),
            costPerMonth.ToString("0.00", inv));

        CostEstimate.Text = L.T("settings.cost.lead") + stats
                            + (polled > 1 ? "\n" + L.T("settings.cost.profiles", polled) : "");
    }
}
