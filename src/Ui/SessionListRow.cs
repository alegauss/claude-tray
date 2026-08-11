using System.ComponentModel;
using System.Windows;

namespace ClaudeTray;

/// <summary>
/// One line of a session's call tree, flattened with its depth carried as an indent.
///
/// <para>Flat rather than a <c>TreeView</c>: the tree is three levels deep at most — task, workflow,
/// agent — and every line has the same five numbers, so a nested control would buy expansion state
/// nobody wants and cost the column alignment that makes the numbers comparable.</para>
/// </summary>
public sealed class TaskLine
{
    internal TaskLine(TaskNode node, int depth)
    {
        Node = node;
        Indent = new Thickness(8 + depth * 18, 0, 0, 0);
        Label = Name(node);
        // Only where §I.1's amendment already permits words: the conversation's opening ask, and a
        // slash command's own arguments. Every other typed prompt is a length, above.
        Prompt = node.Prompt;
        PromptVisibility = node.Prompt.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Calls = Nums.Of(node.SubtreeCalls, "N0");

        long own = node.Own.Input + node.Own.Output + node.Own.CacheCreate;
        TokenBits sub = node.Subtree;
        long all = sub.Input + sub.Output + sub.CacheCreate;
        Own = TokenEstimate.Format((int)Math.Min(int.MaxValue, own));
        // The pair is the whole reading: a coordinator whose own spend is small under a subtree that
        // is large is a fan-out, and one number cannot say that. Blank when they agree, so the column
        // carries information rather than repeating the one beside it.
        Tree = all == own ? "" : TokenEstimate.Format((int)Math.Min(int.MaxValue, all));
    }

    internal TaskNode Node { get; }

    public Thickness Indent { get; }
    public string Label { get; }
    public string Prompt { get; }
    public Visibility PromptVisibility { get; }
    public string Calls { get; }
    public string Own { get; }
    public string Tree { get; }

    private static string Name(TaskNode n) => n.Kind switch
    {
        TaskKind.Command => n.Label,
        TaskKind.Prompt => n.Prompt.Length > 0 ? L.T("stats.tasks.prompt") : L.T("stats.tasks.promptChars", n.Chars),
        TaskKind.Continuation => L.T("stats.tasks.inherited"),
        TaskKind.Workflow => L.T("stats.tasks.workflow", n.Label),
        _ => n.Label,
    };
}

/// <summary>
/// One row of the Sessions list.
///
/// <para>Public, like <see cref="ProjectRow"/> and the view models beside it, because WPF resolves a
/// <c>{Binding}</c> path by reflection over a public type — an internal one binds to nothing, and does
/// it silently.</para>
///
/// <para><b>What a row may say is the whole design (§I.1).</b> Project, clock, duration, turns,
/// tokens — and, since T334, the prompt that opened the conversation, truncated to
/// <see cref="SessionIndex.PromptChars"/>. That last one is the constraint's <em>one</em> amended
/// exception and not a precedent: it is here because nothing else makes a row recognisable. It is read
/// once, truncated before it is stored, and shown under the project it was typed in — because which
/// repo a prompt belongs to is half of recognising it. No other surface in the app gains it, and the
/// session id still sits on the hover for matching against <c>--resume</c>.</para>
/// </summary>
public sealed class SessionListRow : INotifyPropertyChanged
{
    internal SessionListRow(SessionRow row, DateTime nowLocal)
    {
        Row = row;
        DateTime when = DateTimeOffset.FromUnixTimeSeconds((long)row.LastUnix).LocalDateTime;

        Project = row.Name;
        // Today and yesterday read as a time; anything older needs its date. A list whose first
        // column is "19:10" for a conversation last week is a list that has to be counted backwards.
        When = when.Date == nowLocal.Date
            ? when.ToString("HH:mm")
            : when.Date == nowLocal.Date.AddDays(-1)
                ? L.T("stats.sessions.yesterday", when.ToString("HH:mm"))
                : when.ToString("MMM d, HH:mm");
        Duration = Span(row.Seconds);
        Turns = Nums.Of(row.Calls, "N0");
        // Cache reads are out, as everywhere else a token figure is shown here: they are two orders of
        // magnitude larger than the work and would be the only number anyone read.
        Tokens = TokenEstimate.Format((int)Math.Min(int.MaxValue,
            row.Bits.Input + row.Bits.Output + row.Bits.CacheCreate));

        // Already truncated by the index — this is a display, not a second place the cap is decided.
        // Title over prompt where there is one, because a generated label is the narrower thing to
        // show and the shorter thing to read; the prompt drops to a third line and carries the 135
        // transcripts that have no title on its own (T336).
        Title = row.Title.Length > 0 ? row.Title : row.Prompt;
        TitleVisibility = Title.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Prompt = row.Title.Length > 0 ? row.Prompt : "";
        PromptVisibility = Prompt.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // The hover carries the identifiers, which is what makes a row actionable without making it
        // descriptive: the id is what `claude --resume` takes.
        Tip = L.T("stats.sessions.tip", row.Session,
                  row.Models.Length > 0 ? string.Join(", ", row.Models) : "—",
                  row.Agents);
    }

    internal SessionRow Row { get; }

    public string Project { get; }
    /// <summary>What the conversation is called: its generated title, or its opening prompt where
    /// there is no title. Never both on this line.</summary>
    public string Title { get; }
    public Visibility TitleVisibility { get; }
    /// <summary>The opening prompt, on its own third line and only when a title took the second one.
    /// Collapsed otherwise, so a row is the same height as its neighbours rather than carrying an
    /// empty line or the same text twice.</summary>
    public string Prompt { get; }
    public Visibility PromptVisibility { get; }
    public string When { get; }
    public string Duration { get; }
    public string Turns { get; }
    public string Tokens { get; }
    public string Tip { get; }

    // ---- the drill-down (T329), filled on the first expand and kept after ----

    private IReadOnlyList<TaskLine>? _detail;
    private bool _open;

    /// <summary>The call tree under this conversation, or null until it has been asked for. Walking a
    /// session costs a few files, so it is walked when a row is opened rather than for all 549 of
    /// them — and kept afterwards, because closing a row is not a reason to forget.</summary>
    public IReadOnlyList<TaskLine>? Detail
    {
        get => _detail;
        internal set { _detail = value; Raise(nameof(Detail)); Raise(nameof(DetailVisibility)); }
    }

    /// <summary>Whether this row is showing its tree.</summary>
    public bool Open
    {
        get => _open;
        internal set { _open = value; Raise(nameof(Open)); Raise(nameof(DetailVisibility)); Raise(nameof(Chevron)); }
    }

    public Visibility DetailVisibility => _open && _detail is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The disclosure glyph, from the icon font the rest of the window uses. Down means
    /// "there is more under this", up means "it is showing" — the first capture
    /// had them the other way round, which reads as an instruction to do what has already been done.</summary>
    public string Chevron => _open ? "" : "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Minutes under an hour, hours over it. One unit that reads well at both ends of "three
    /// minutes" and "an afternoon" does not exist, and rounding an afternoon to 380 minutes is worse
    /// than switching.</summary>
    /// <remarks>Through <see cref="Nums"/>, like every other number this app puts in front of a reader
    /// (T216) — the first capture of this pane read "1,2 h" in the English screenshot, which is the
    /// exact defect that rule exists for. A static formatter rather than an inline interpolation so
    /// <c>--selftest</c>'s format sweep can reach it; inline is what keeps a number out of it.</remarks>
    private static string Span(double seconds)
        => seconds >= 3600
            ? L.T("stats.sessions.hours", Nums.Of(seconds / 3600, "0.0"))
            : L.T("stats.sessions.minutes", Nums.Of(Math.Max(1, Math.Round(seconds / 60)), "0"));
}
