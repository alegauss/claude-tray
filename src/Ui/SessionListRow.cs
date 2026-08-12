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

        // The subtree's mix, not the node's own: a fan-out's agents run at their own levels, and the
        // line the reader is looking at is the whole branch (T331).
        Effort = EffortMix.Line(node.SubtreeEfforts);
        EffortVisibility = Effort.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal TaskNode Node { get; }

    public Thickness Indent { get; }
    public string Label { get; }
    public string Prompt { get; }
    public Visibility PromptVisibility { get; }
    public string Calls { get; }
    public string Own { get; }
    public string Tree { get; }
    /// <summary>The effort this branch ran at — one level, or the mix with each level's share.</summary>
    public string Effort { get; }
    public Visibility EffortVisibility { get; }

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
                : when.ToString("MMM d, HH:mm", L.DateCulture);
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

        // Effort is the largest lever on what a conversation costs and it buys calls rather than
        // longer answers, so two rows of the same length can differ several-fold with nothing else on
        // screen saying why. Shown as the mix it ran at, never as advice about it (T331).
        Effort = EffortMix.Line(row.Efforts);
        EffortVisibility = Effort.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // What those tokens come to at published API rates (T346). Tokens rank the list; this explains
        // it — a $2 conversation beside a $40 one is a sentence a token count cannot say, because the
        // dear model and the cheap one are the same number of tokens. Never the word "cost": the app
        // does not know what anyone pays, and §I.7 is why it must not imply otherwise.
        ListPrices.Equivalent price = ListPrices.Of(row.PerModel);
        Money = price.PricedTokens > 0 ? L.T("stats.sessions.money", Nums.Of(price.Dollars, "0.00")) : "";
        // Two different silences, and the hover is where they are told apart. A row the table could
        // price in part is a floor and says so; one it could not price at all leaves the column empty,
        // and the reason is the model id — which is on this hover and nowhere else.
        MoneyTip = price.PricedTokens == 0
            ? L.T("stats.sessions.moneyNone")
            : price.Complete
                ? L.T("stats.sessions.moneyTip", ListPrices.Read.ToString("yyyy-MM-dd"))
                : L.T("stats.sessions.moneyPartial", ListPrices.Read.ToString("yyyy-MM-dd"));

        // The hover carries the identifiers, which is what makes a row actionable without making it
        // descriptive: the id is what `claude --resume` takes.
        Tip = L.T("stats.sessions.tip", row.Session,
                  row.Models.Length > 0 ? string.Join(", ", row.Models) : "—",
                  row.Agents);
    }

    /// <summary>The conversation's tokens at API list prices, or empty where the rate table knew none of
    /// the models that answered. Always shown with its own qualifier — see <see cref="ListPrices"/>.</summary>
    public string Money { get; } = "";
    /// <summary>Why the figure is what it is: the date the rates were read, and whether the total covers
    /// every model in the conversation.</summary>
    public string MoneyTip { get; } = "";

    /// <summary>The effort levels this conversation's calls ran at, with each level's share where it
    /// ran at more than one. Empty for a session whose lines named none.</summary>
    public string Effort { get; } = "";
    public Visibility EffortVisibility { get; } = Visibility.Collapsed;

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

/// <summary>
/// One row of the by-kind table under the Sessions list (T333): what kind of work it was, how many
/// tasks, what a usual one cost, and what the kind cost altogether.
///
/// <para>Public for the same reason every view model here is: WPF resolves a <c>{Binding}</c> path by
/// reflection over a public type, and an internal one binds to nothing and does it silently.</para>
/// </summary>
public sealed class WorkKindRow
{
    internal WorkKindRow(WorkGroup group, long rangeTotal)
    {
        Label = WorkKinds.Label(group);
        Tasks = Nums.Of(group.Tasks, "N0");
        Median = TokenEstimate.Format((int)Math.Min(int.MaxValue, group.Median));
        Total = TokenEstimate.Format((int)Math.Min(int.MaxValue, group.Total));
        // Of what the table itself sums to, so the column adds to 100% and never to a figure the
        // reader cannot see on screen.
        Share = rangeTotal > 0 ? Nums.Of((double)group.Total * 100 / rangeTotal, "0") + "%" : "";
    }

    public string Label { get; }
    public string Tasks { get; }
    public string Median { get; }
    public string Total { get; }
    public string Share { get; }
}
