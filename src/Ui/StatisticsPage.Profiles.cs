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
    /// True once the picker has been filled at least once. The first fill and every later one are
    /// different events, and only the second kind may repaint the window — see <see cref="SetProfiles"/>.
    /// </summary>
    private bool _pickerFilled;

    /// <summary>
    /// Offer a profile picker (T128), and keep it in step with the tray. Called with everything the tray
    /// watches, monitored first; with one profile the picker stays hidden and nothing about the window
    /// changes.
    ///
    /// <para><b>Called again whenever the icon changes hands (T319).</b> It used to be called once, when
    /// the window was built, and the whole page then rested on "index 0 is the account the poll is about"
    /// — a fact that stops being true the moment a menu pick or auto-follow (T126) moves the icon while
    /// the window sits open. Nothing rebuilt the list, so every poll went on pushing the *new* monitored
    /// account's reading into the entry still labelled with the *old* one: a work account's 86% drawn
    /// under a personal account's name, while picking the account actually being worked in showed its
    /// own — correct — series and read as the empty one.</para>
    ///
    /// <para>So the rebuild carries the user's pick across by <em>key</em> rather than by index: the list
    /// is reordered under it, and position 1 after a switch is not the profile position 1 named before it.
    /// A window that was following the icon keeps following it.</para>
    /// </summary>
    internal void SetProfiles(List<ClaudeInfo> profiles)
    {
        // The tray re-discovers on every menu open (T137), so most calls say nothing has changed. Answered
        // here rather than at the call site: rebuilding the items would shut an open dropdown, and only
        // this page knows what it is currently showing. Same accounts, same order, same names ⇒ same picker.
        if (_pickerFilled && Same(profiles, _profiles)) return;

        _profiles = profiles;

        int index = PickerIndex(profiles, _profile.Key, _showingMonitored);
        bool wasMonitored = _showingMonitored;
        string wasKey = _profile.Key;
        if (profiles.Count > 0)
        {
            _profile = new ProfileRef(ProfileStore.KeyFor(profiles[index]), profiles[index].ConfigDir);
            _showingMonitored = index == 0;
        }

        if (StatsProfileCombo is not null)
        {
            ProfileCard.Visibility = profiles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            StatsProfileCombo.SelectionChanged -= Profile_Changed;
            StatsProfileCombo.Items.Clear();
            foreach (ClaudeInfo p in profiles)
                StatsProfileCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = p.Label });
            if (profiles.Count > 0) StatsProfileCombo.SelectedIndex = index;
            StatsProfileCombo.SelectionChanged += Profile_Changed;
        }

        // The first fill is the page being told what it already is: it was constructed with a reading and
        // has rendered it, and re-deriving here would throw that away. That is not a nicety — the preview
        // paths (`--stats`, `--capture-stats`) fill the picker *after* building the page around a
        // synthetic reading, and the fixture is the entire point of the picture.
        if (!_pickerFilled)
        {
            _pickerFilled = true;
            return;
        }

        if (profiles.Count == 0 || (_profile.Key == wasKey && _showingMonitored == wasMonitored)) return;

        // What the tray last pushed belongs to whichever account was monitored when it was pushed, and the
        // rebuild above has just said that is not the account on screen any more. Dropped rather than
        // re-labelled: `ShowProfile` falls back to this profile's own last logged reading, which is the
        // same poll's line — `RecordReading` writes it before the push — so nothing is actually lost.
        _monitoredSnapshot = null;
        _monitoredError = null;
        ShowProfile(profiles[index]);
    }

    /// <summary>
    /// Where the window lands in a rebuilt watch list. Following the icon means index 0 — the list is
    /// monitored-first — and a profile the user picked by hand is found again by <paramref name="shownKey"/>,
    /// never by the position it held before: the switch that triggers the rebuild is exactly what reorders
    /// the list, so position 1 afterwards is not the profile position 1 named before it. Falling back to 0
    /// covers the only way the lookup can miss — the picked profile is no longer on the machine.
    ///
    /// <para>Static and over keys alone so the decision this defect was (T319) can be asserted without a
    /// window: it is the whole of what went wrong, and it is not otherwise reachable from a check.</para>
    /// </summary>
    internal static int PickerIndex(List<ClaudeInfo> profiles, string shownKey, bool followingMonitored)
    {
        if (followingMonitored || profiles.Count == 0) return 0;
        int found = profiles.FindIndex(p => ProfileStore.KeyFor(p) == shownKey);
        return found >= 0 ? found : 0;
    }

    /// <summary>Two watch lists that would build the same picker: the same accounts, in the same order,
    /// under the same labels. The label is part of it because renaming a profile in Settings changes
    /// nothing else about the list and is the whole of what the picker would have to show.</summary>
    internal static bool Same(List<ClaudeInfo> a, List<ClaudeInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (ProfileStore.KeyFor(a[i]) != ProfileStore.KeyFor(b[i]) || a[i].Label != b[i].Label)
                return false;
        return true;
    }

    /// <summary>Preview-only: pick a profile by index, so the capture path can render the window as
    /// another profile without a click.</summary>
    internal void SelectProfileForPreview(int index)
    {
        if (StatsProfileCombo is not null && index >= 0 && index < StatsProfileCombo.Items.Count)
            StatsProfileCombo.SelectedIndex = index;
    }

    private void Profile_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        int i = StatsProfileCombo.SelectedIndex;
        if (i < 0 || i >= _profiles.Count) return;

        ClaudeInfo picked = _profiles[i];
        _profile = new ProfileRef(ProfileStore.KeyFor(picked), picked.ConfigDir);
        _showingMonitored = i == 0;
        ShowProfile(picked);
    }

    /// <summary>
    /// Move the whole window onto <paramref name="picked"/> — the profile <see cref="_profile"/> and
    /// <see cref="_showingMonitored"/> now name. Reached from the picker and from a rebuild the tray
    /// asked for, because a switch arriving by either route has the same work to do.
    /// </summary>
    private void ShowProfile(ClaudeInfo picked)
    {
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
        // `_snapshot` is whatever the profile being left behind put there (T164). Its own store is the
        // fallback for the poll that has not landed yet — a switch drops what was held about the outgoing
        // account, and the incoming one is not blank just because this tray has not polled it *since*.
        _snapshot = (_showingMonitored ? _monitoredSnapshot : null)
            ?? (UsageHistory.Latest(_profile.Key) is { } h
                ? new PaceSnapshot(h.Util5h, h.Reset5h, h.Util7d, h.Reset7d, h.Extra, h.ResetExtra)
                : null);

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
