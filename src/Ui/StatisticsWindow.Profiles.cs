using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

// This is a WinForms + WPF hybrid, so System.Drawing and System.Windows.Media both contribute a
// Brush / Color / Point / Size. Pin these names to the WPF (Media) types the charts are drawn with.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ClaudeTray;

/// <summary>Part of <see cref="StatisticsWindow"/> — split out by T133, moved verbatim.</summary>
internal partial class StatisticsWindow : Window
{
    /// <summary>
    /// Offer a profile picker (T128). Called by the tray with everything it watches, monitored first;
    /// with one profile the picker stays hidden and nothing about the window changes.
    /// </summary>
    internal void SetProfiles(List<ClaudeInfo> profiles)
    {
        _profiles = profiles;
        if (profiles.Count > 0)
        {
            _profile = new ProfileRef(ProfileStore.KeyFor(profiles[0]), profiles[0].ConfigDir);
            _showingMonitored = true;
        }
        if (ProfileCombo is null) return;

        ProfileCard.Visibility = profiles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ProfileCombo.SelectionChanged -= Profile_Changed;
        ProfileCombo.Items.Clear();
        foreach (ClaudeInfo p in profiles)
            ProfileCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = p.Label });
        if (profiles.Count > 0) ProfileCombo.SelectedIndex = 0;
        ProfileCombo.SelectionChanged += Profile_Changed;
    }

    /// <summary>Preview-only: pick a profile by index, so the capture path can render the window as
    /// another profile without a click.</summary>
    internal void SelectProfileForPreview(int index)
    {
        if (ProfileCombo is not null && index >= 0 && index < ProfileCombo.Items.Count)
            ProfileCombo.SelectedIndex = index;
    }

    private void Profile_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        int i = ProfileCombo.SelectedIndex;
        if (i < 0 || i >= _profiles.Count) return;

        ClaudeInfo picked = _profiles[i];
        _profile = new ProfileRef(ProfileStore.KeyFor(picked), picked.ConfigDir);
        _showingMonitored = i == 0;

        // Abandon any computation still in flight for the profile we are leaving. `Reload` stamps each
        // run with a generation and drops a result whose stamp is stale, but nothing bumped it here — so
        // the previous profile's report would land *after* the switch and repaint the window with another
        // account's charts under this account's name. Caught by capturing the switch: the picker said
        // "Pessoal" while the curve was still the other profile's 74%.
        _generation++;

        // A profile the tray isn't polling for the icon has no live reading in hand — but it has its own
        // stored series (T125), and its last logged reading is exactly as fresh as its last poll. That is
        // the honest snapshot to draw it from; the footer timestamp says when it was taken.
        _snapshot = _showingMonitored
            ? _snapshot
            : UsageHistory.Latest(_profile.Key) is { } h
                ? new PaceSnapshot(h.Util5h, h.Reset5h, h.Util7d, h.Reset7d)
                : null;

        // The live strip is a different config dir's transcripts now.
        StopLive(dispose: true);
        StartLive();

        if (_snapshot is not null) Reload();
        else ShowStatus(L.T("stats.noProfileData", picked.Label));
    }
}
