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

/// <summary>Part of <see cref="StatisticsPage"/> — split out by T133, moved verbatim.</summary>
internal partial class StatisticsPage
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
        // the honest snapshot to draw it from; the footer timestamp says when it was taken. The monitored
        // profile is restored from where the poll loop keeps it, **never** from `_snapshot`: by this point
        // `_snapshot` is whatever the profile being left behind put there (T164).
        _snapshot = _showingMonitored
            ? _monitoredSnapshot
            : UsageHistory.Latest(_profile.Key) is { } h
                ? new PaceSnapshot(h.Util5h, h.Reset5h, h.Util7d, h.Reset7d, h.Extra, h.ResetExtra)
                : null;

        // The banner belongs to the reading it came with. "The live API is unavailable" is a fact about
        // the profile the tray polls; carrying it over a stored reading of another account would blame
        // that account for an outage nothing tried to reach on its behalf.
        _error = _showingMonitored ? _monitoredError : null;

        // Both windows' last-rendered pace go with it. They are kept so a resize can redraw without a
        // rescan (and the Throughput tab reads them for its two window averages) — which makes them the
        // *previous* profile's curves from here until the new report lands, and permanently if this
        // profile has nothing to report. Clearing them is also what puts "computing…" back up: with a
        // report on screen T118 deliberately leaves it there, and that rule is about a refresh of the
        // same profile, not about a switch to another one.
        _session = null;
        _weekly = null;

        // The live strip is a different config dir's transcripts now.
        StopLive(dispose: true);
        StartLive();

        if (_snapshot is null)
        {
            ShowStatus(L.T("stats.noProfileData", picked.Label));
            return;
        }

        // Put this profile's own last report back up while the new one is computed (T166), so the round
        // trip the field report is about is instant instead of a blank pane for the length of a transcript
        // scan. Deliberately *after* the live strip was reset: what comes back is the pace panes, never
        // the drawn live history — that one is appended by construction (T119) and splicing another
        // profile's minute onto it is the third defect T164 fixed.
        //
        // Rendering it also refills `_session`/`_weekly`, so the `Reload` below sees a report on screen and
        // leaves it there rather than swapping in "computing…" (T118's rule, now reaching the switch too).
        // The footer is what keeps this honest: `Render` stamps it from the report's own `ComputedLocal`,
        // so a re-shown view says when it was measured rather than claiming to be now.
        if (_lastReport.TryGetValue(_profile.Key, out PaceReport? cached) &&
            (DateTime.Now - cached.ComputedLocal).TotalSeconds is >= 0 and <= CachedReportMaxAgeSeconds)
        {
            Render(cached);
        }

        Reload();
    }
}
