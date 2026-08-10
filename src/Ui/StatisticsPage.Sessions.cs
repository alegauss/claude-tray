using System.Windows;
using System.Windows.Controls;

namespace ClaudeTray;

/// <summary>
/// Part of <see cref="StatisticsPage"/> — the Sessions pane (T328).
///
/// <para>The other three panes answer <em>how much is left</em> and <em>how fast is it going</em>.
/// This one answers <em>where did it go</em>, and that question is only askable against a list of
/// nameable things: the projects strip is the nearest thing today and it names four repos and an
/// "others" bucket, which is the right resolution for a chart and the wrong one for a search.</para>
///
/// <para>The list is the destination. What a row opens into is T329's, and keeping them apart is
/// deliberate — a list nobody can read is a failure a drill-down would hide.</para>
/// </summary>
internal partial class StatisticsPage
{
    /// <summary>Index of the Sessions tab in <c>PanesBody</c>.</summary>
    private const int SessionsTab = 3;

    /// <summary>How the list is ordered. Clock is the default because recognising a conversation
    /// starts from when it ran; tokens is the other question a list of conversations is opened with.</summary>
    private enum SessionSort { Clock, Tokens }

    private SessionSort _sessionSort = SessionSort.Clock;
    /// <summary>The whole index, kept so the range picker and the sort re-filter rather than re-scan —
    /// a rescan is seconds on a cold cache and none of it is needed to answer a narrower question.</summary>
    private IReadOnlyList<SessionRow>? _sessions;
    private int _sessionsGeneration;

    /// <summary>Days the list is bounded to, or 0 for everything on disk. Seven by default: the index
    /// covers every transcript there is, and a machine with 549 conversations should not render 549
    /// rows to answer a question about today.</summary>
    private int SessionDays => SessionsRange.SelectedIndex switch { 1 => 30, 2 => 0, _ => 7 };

    private void WireSessionsTab()
    {
        // From the system metric rather than a number that looks about right. Two insets, not one,
        // because this theme's scrollbar is an *overlay*: it does not take width from the content, so
        // reserving it in the header alone moved the headings left of their values and left the thumb
        // sitting on top of the first two rows' numbers. Both captures are why this is measured.
        double gutter = SystemParameters.VerticalScrollBarWidth;
        SessionsGutter.Width = new GridLength(gutter);
        SessionList.Margin = new Thickness(0, 0, gutter, 0);
        SessionsRange.SelectionChanged += (_, _) => RenderSessions();
        SortByWhen.Click += (_, _) => { _sessionSort = SessionSort.Clock; RenderSessions(); };
        SortByTokens.Click += (_, _) => { _sessionSort = SessionSort.Tokens; RenderSessions(); };
    }

    /// <summary>
    /// Load the index for the shown profile, off the UI thread.
    ///
    /// <para>Off it because the scan is not free and its cost is the honest kind: measured 4.5s over
    /// 1.2 GB with a cold cache and 117ms with a warm one, so the common case is instant and the first
    /// run of a new install is not. Blocking the dispatcher for either would freeze the window while it
    /// claims to be showing a list.</para>
    /// </summary>
    private void LoadSessions()
    {
        int gen = ++_sessionsGeneration;
        _sessions = null;
        SessionList.ItemsSource = null;
        SessionsEmpty.Text = L.T("stats.sessions.loading");
        SessionsEmpty.Visibility = Visibility.Visible;
        SessionsCount.Text = "";

        ProfileRef profile = _profile;
        System.Threading.Tasks.Task.Run(() =>
        {
            IReadOnlyList<SessionRow> rows;
            try { rows = SessionIndex.Load(profile); }
            catch { rows = Array.Empty<SessionRow>(); }   // an unreadable tree is an empty list, not a crash
            Dispatcher.BeginInvoke(() =>
            {
                // A profile switched, or the page reloaded, while the scan ran: this answer is about a
                // question nobody is asking any more.
                if (gen != _sessionsGeneration) return;
                _sessions = rows;
                RenderSessions();
            });
        });
    }

    /// <summary>
    /// Block the capture path until the list is a list. The scan runs off the UI thread and lands
    /// through <see cref="System.Windows.Threading.Dispatcher.BeginInvoke(Delegate)"/>, so a capture
    /// taken the moment the tab is selected photographs "Reading your transcripts…" and reports a PNG
    /// written — which is the defect T286 and T298 are open about, and there is no reason to add a
    /// third instance of it.
    ///
    /// <para>Pumped rather than waited on: a blocking wait on this thread would deadlock against the
    /// very callback it is waiting for. Bounded, because a capture that never returns is worse than one
    /// that shows the honest in-progress state.</para>
    /// </summary>
    internal void WaitForSessions(int millis = 30_000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(millis);
        while (_sessions is null && DateTime.UtcNow < deadline)
        {
            // Draining to Background lets the Normal-priority callback that carries the scan's result
            // run — the same "let the queue breathe" move a modal dialog makes.
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            System.Threading.Thread.Sleep(25);
        }
        UpdateLayout();
    }

    private void RenderSessions()
    {
        if (_sessions is not { } all)
            return;   // still scanning; LoadSessions calls back into here when it lands

        DateTime nowLocal = DateTime.Now;
        double floor = SessionDays > 0
            ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() - SessionDays * 86400.0
            : 0;

        IEnumerable<SessionRow> shown = all.Where(r => r.LastUnix >= floor);
        shown = _sessionSort == SessionSort.Tokens
            ? shown.OrderByDescending(r => r.Bits.Input + r.Bits.Output + r.Bits.CacheCreate)
            : shown.OrderByDescending(r => r.LastUnix);

        List<SessionListRow> rows = shown.Select(r => new SessionListRow(r, nowLocal)).ToList();
        SessionList.ItemsSource = rows;

        // Empty is a state with a sentence, not a frame drawn around nothing — and the two ways to be
        // empty are different facts: no transcripts at all, or none inside the range that is selected.
        bool empty = rows.Count == 0;
        SessionsEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
            SessionsEmpty.Text = L.T(all.Count == 0 ? "stats.sessions.none" : "stats.sessions.noneInRange");
        SessionsCount.Text = empty ? "" : L.T("stats.sessions.count", rows.Count);

        // Which column the list is ordered by, said rather than implied: two headers that look alike
        // and one of them is doing something.
        SortByWhen.FontWeight = _sessionSort == SessionSort.Clock ? FontWeights.SemiBold : FontWeights.Normal;
        SortByTokens.FontWeight = _sessionSort == SessionSort.Tokens ? FontWeights.SemiBold : FontWeights.Normal;
    }
}
