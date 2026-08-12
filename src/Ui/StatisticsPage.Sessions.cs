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
        // The whole row opens it, not a hit-target the size of a chevron: the glyph says a row can be
        // opened and the row is what you click.
        SessionList.AddHandler(System.Windows.Input.Mouse.MouseUpEvent,
                               new System.Windows.Input.MouseButtonEventHandler(Row_Clicked), handledEventsToo: true);

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
            try { rows = SessionIndex.Load(profile, PreviewSessionsRoot); }
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
    /// Open or close a conversation's call tree (T329).
    ///
    /// <para>The walk is off the UI thread for the same reason the index's is, though not for the
    /// same size: a session is a handful of files against the index's whole tree, and the cost that
    /// actually bites is a conversation with a large fan-out. It is walked once and kept, so closing
    /// and reopening a row is free.</para>
    /// </summary>
    private void Row_Clicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        if ((e.OriginalSource as DependencyObject) is not { } hit) return;

        // Up the visual tree to the row this pixel belongs to, because the click lands on a TextBlock.
        SessionListRow? row = null;
        for (DependencyObject? d = hit; d != null && row == null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: SessionListRow r }) row = r;
        if (row == null) return;

        if (row.Detail != null) { row.Open = !row.Open; return; }

        row.Open = true;
        SessionRow model = row.Row;
        ProfileRef profile = _profile;
        System.Threading.Tasks.Task.Run(() =>
        {
            IReadOnlyList<TaskNode> tasks;
            try { tasks = SessionTasks.For(model, profile, PreviewSessionsRoot); }
            catch { tasks = Array.Empty<TaskNode>(); }   // an unreadable transcript closes the row, never crashes
            var lines = new List<TaskLine>();
            foreach (TaskNode t in tasks) Flatten(t, 0, lines);
            Dispatcher.BeginInvoke(() => row.Detail = lines);
        });
    }

    /// <summary>
    /// A click on the live strip names what is burning: it moves to the Sessions pane and opens the
    /// conversation that is running (T337).
    ///
    /// <para>T329's design asked for the opposite — a selected task lighting up the strip — and this is
    /// the same join in the direction that has data. Nothing happens when nothing is running, which is
    /// the honest answer: jumping to a conversation that ended yesterday would name the wrong thing for
    /// a spike that is not there.</para>
    /// </summary>
    private void LiveChart_Clicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        if (_sessions is not { } rows) { LoadSessions(); return; }

        double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (SessionIndex.Running(rows, now, LiveRate.HistorySeconds) is not { } running) return;

        // The pane the row lives in, before the row: selecting inside a tab nobody is looking at is a
        // jump the user never sees happen.
        PanesBody.SelectedIndex = SessionsTabIndex;
        Dispatcher.BeginInvoke(() => OpenSession(running), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Which tab the Sessions pane is, named once rather than counted at each call site.</summary>
    private const int SessionsTabIndex = 3;

    /// <summary>Open one conversation's call tree and bring it into view — the shared half of a click on
    /// the row and a click on the live chart.</summary>
    private void OpenSession(SessionRow model)
    {
        if (SessionList.ItemsSource is not IEnumerable<SessionListRow> shown) return;
        SessionListRow? row = shown.FirstOrDefault(r => r.Row.Session == model.Session &&
                                                        r.Row.Project == model.Project);
        if (row == null) return;

        // An ItemsControl, not a ListBox: it has no ScrollIntoView, so the row's own container is
        // asked to come into view. Null while the container has not been realised, which is a
        // best-effort scroll and never a crash.
        if (SessionList.ItemContainerGenerator.ContainerFromItem(row) is FrameworkElement container)
            container.BringIntoView();
        if (row.Detail != null) { row.Open = true; return; }

        row.Open = true;
        ProfileRef profile = _profile;
        System.Threading.Tasks.Task.Run(() =>
        {
            IReadOnlyList<TaskNode> tasks;
            try { tasks = SessionTasks.For(model, profile, PreviewSessionsRoot); }
            catch { tasks = Array.Empty<TaskNode>(); }
            var lines = new List<TaskLine>();
            foreach (TaskNode t in tasks) Flatten(t, 0, lines);
            Dispatcher.BeginInvoke(() => row.Detail = lines);
        });
    }

    private static void Flatten(TaskNode node, int depth, List<TaskLine> into)
    {
        into.Add(new TaskLine(node, depth));
        foreach (TaskNode child in node.Children) Flatten(child, depth + 1, into);
    }

    /// <summary>
    /// Block the capture path until the list is a list — true when the scan landed, false when the pane
    /// is still showing "Reading your transcripts…" at the deadline. The scan runs off the UI thread and
    /// lands through <see cref="System.Windows.Threading.Dispatcher.BeginInvoke(Delegate)"/>, so a
    /// capture taken the moment the tab is selected photographs the placeholder and reports a PNG
    /// written — which is the defect T286 and T298 are open about, and there is no reason to add a
    /// third instance of it.
    ///
    /// <para>Pumped rather than waited on: a blocking wait on this thread would deadlock against the
    /// very callback it is waiting for. Bounded, because a capture that never returns is worse than one
    /// that answers.</para>
    ///
    /// <para><b>One rule, shared with <see cref="StatisticsPage.WaitForReport"/> (T343).</b> Both waits
    /// report what they found and neither decides: a deadline means the content did not arrive, and a
    /// PNG of the placeholder is not a slower picture of the pane, it is none (§XX.6). This one used to
    /// return void and proceed, so tab four was captured mid-scan and the run still printed
    /// <c>wrote …-sessions.png</c> and exited 0. What differs between the two is the caller's reach, not
    /// the meaning of the bound: the report is the whole page and nothing is written, the sessions list
    /// is one pane of four and only that one is omitted.</para>
    /// </summary>
    internal bool WaitForSessions(int millis = 30_000)
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
        return _sessions is not null;
    }

    /// <summary>Open the newest conversation's call tree and wait for it, so a capture of this pane
    /// shows what the pane <em>does</em> rather than a list of rows that look inert (T329). Same pump,
    /// same bound, same reason as <see cref="WaitForSessions"/>.</summary>
    internal void OpenFirstSession(int millis = 20_000)
    {
        if (SessionList.ItemsSource is not IEnumerable<SessionListRow> rows) return;
        SessionListRow? first = rows.FirstOrDefault();
        if (first == null) return;

        first.Open = true;
        SessionRow model = first.Row;
        ProfileRef profile = _profile;
        System.Threading.Tasks.Task.Run(() =>
        {
            IReadOnlyList<TaskNode> tasks;
            try { tasks = SessionTasks.For(model, profile, PreviewSessionsRoot); }
            catch { tasks = Array.Empty<TaskNode>(); }
            var lines = new List<TaskLine>();
            foreach (TaskNode t in tasks) Flatten(t, 0, lines);
            Dispatcher.BeginInvoke(() => first.Detail = lines);
        });

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(millis);
        while (first.Detail == null && DateTime.UtcNow < deadline)
        {
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
        SessionsPeak.Text = empty ? "" : PeakLine(shown, floor);

        // Which kind of work, over the same rows and the same range as everything else in this pane.
        // A task is in range when it *started* in it: a task is the unit being counted, and half of
        // one is not a task (T333).
        IReadOnlyList<WorkGroup> kinds = empty
            ? Array.Empty<WorkGroup>()
            : WorkKinds.Of(shown.SelectMany(r => r.Tasks ?? (IReadOnlyList<TaskRow>)Array.Empty<TaskRow>())
                                .Where(k => k.First >= floor));
        long kindTotal = WorkKinds.Total(kinds);
        SessionKinds.ItemsSource = kinds.Select(g => new WorkKindRow(g, kindTotal)).ToList();

        // The heaviest five hours across exactly what is listed, swept from the per-minute series the
        // rows carry (T332). Over the shown rows rather than the whole index, so changing the range
        // changes the reading — a peak from outside the list would be a number about nothing on screen.
        static string PeakLine(IEnumerable<SessionRow> shown, double floor)
        {
            // Clamped to the range, not merely to the sessions in it. A conversation is listed when its
            // *last* turn falls inside the range, and a long one carries minutes from before that — read
            // unclamped, a seven-day range reported nine active days, which is the picker contradicting
            // itself on screen. Seen in the capture, not in a check (T332).
            long from = (long)(floor / 60);
            var minutes = new Dictionary<long, long>();
            foreach (SessionRow r in shown)
            {
                if (r.Minutes is null) continue;
                foreach (KeyValuePair<long, long> kv in r.Minutes)
                {
                    if (kv.Key < from) continue;
                    minutes[kv.Key] = minutes.TryGetValue(kv.Key, out long had) ? had + kv.Value : kv.Value;
                }
            }

            PeakWindow peak = HeaviestWindow.Of(minutes);
            if (!peak.Found) return "";

            // Through the UI language's culture, not the machine's: an English window rendering a
            // Portuguese month is what the published capture caught this doing (T335).
            string when = peak.StartLocal.ToString("MMM d, HH:mm", L.DateCulture);
            string head = L.T("stats.sessions.peak", TokenEstimate.Format(
                (int)Math.Min(int.MaxValue, peak.Tokens)), when);

            // The comparison is what makes the peak a fact rather than a large number — but two days
            // is not a usual day, so below a week of them the peak stands alone.
            // Three days is a thin "usual day" and the line says how many it stands on, so the reader
            // can weigh it. Below three there is no usual day to speak of and the peak stands alone.
            long median = HeaviestWindow.MedianDayPeak(minutes);
            int days = HeaviestWindow.ActiveDays(minutes);
            if (median <= 0 || days < 3) return head;

            return head + "  ·  " + L.T("stats.sessions.peakMedian",
                TokenEstimate.Format((int)Math.Min(int.MaxValue, median)), days);
        }

        // Which column the list is ordered by, said rather than implied: two headers that look alike
        // and one of them is doing something.
        SortByWhen.FontWeight = _sessionSort == SessionSort.Clock ? FontWeights.SemiBold : FontWeights.Normal;
        SortByTokens.FontWeight = _sessionSort == SessionSort.Tokens ? FontWeights.SemiBold : FontWeights.Normal;
    }
}
