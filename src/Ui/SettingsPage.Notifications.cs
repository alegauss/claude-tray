using System.Windows;

namespace ClaudeTray;

/// <summary>Part of <see cref="SettingsPage"/> — one page per file, split out by T134, moved verbatim.</summary>
internal partial class SettingsPage
{
    private void WeeklyMinSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateWeeklyMinLabel();

    private void SessionMinSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateSessionMinLabel();

    // The threshold labels read as a sentence with the leading "Only notify above" caption:
    // "above 2%". The value labels can be null while the slider value is set in InitializeComponent.
    private void UpdateWeeklyMinLabel()
    {
        if (WeeklyMinValue is null) return;
        WeeklyMinValue.Text = $"{(int)Math.Round(WeeklyMinSlider.Value)}%";
    }

    private void UpdateSessionMinLabel()
    {
        if (SessionMinValue is null) return;
        SessionMinValue.Text = $"{(int)Math.Round(SessionMinSlider.Value)}%";
    }

    private void ContextThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateContextThresholdLabel();

    // Shown the same way the window shows every other token count, so the threshold reads in the same
    // units as the gauge it is about.
    private void UpdateContextThresholdLabel()
    {
        if (ContextThresholdValue is null) return;
        ContextThresholdValue.Text = TokenEstimate.Format((int)Math.Round(ContextThresholdSlider.Value));
    }
}
