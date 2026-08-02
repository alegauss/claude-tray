using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// WinForms and WPF each contribute a Brush / Color / Orientation / HorizontalAlignment; pin these
// names to the WPF ones the gauge is drawn with (same convention as StatisticsPage).
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace ClaudeTray;

/// <summary>One kind of context source — Instructions, Memory, Skills, Agents — and its files.</summary>
public sealed class SourceGroup
{
    private SourceGroup(ContextKind kind, List<ContextSource> sources, RowStyle style, SourceSort sort,
        UsageEvidence? evidence, DateTime nowUtc, HashSet<string> selected)
    {
        // The count rides in the header rather than in a "{0} files" string: it needs no plural rule
        // in any of the five languages, and it leaves the Load column to the rows, where the
        // eager/lazy word actually means something.
        Header = $"{ContextText.Kind(kind)}  ({sources.Count})";
        Size = ContextText.Size(sources.Sum(s => s.Bytes));
        Tokens = TokenEstimate.Format(sources.Sum(s => s.Tokens));
        int eager = sources.Sum(s => s.EagerTokens);
        // A whole group that costs nothing every session (memory bodies, settings files) reads as an
        // em dash like its rows do — "≈0" invites the question of whether it is a rounding artifact.
        Eager = eager > 0 ? TokenEstimate.Format(eager) : "—";
        Items = Sorted(sources, sort).Select(s => new SourceRow(s, style, evidence, nowUtc, selected)).ToList();
    }

    private static IEnumerable<ContextSource> Sorted(List<ContextSource> sources, SourceSort sort)
        => sort switch
        {
            SourceSort.Size => sources.OrderByDescending(s => s.Bytes).ThenBy(s => s.Label),
            SourceSort.Age => sources.OrderBy(s => s.ModifiedUtc).ThenBy(s => s.Label),
            // Eager first, then size — so a group of all-lazy bodies still reads biggest-first
            // rather than in whatever order the directory happened to enumerate.
            _ => sources.OrderByDescending(s => s.EagerTokens).ThenByDescending(s => s.Bytes),
        };

    public string Header { get; }
    public string Size { get; }
    public string Tokens { get; }
    public string Eager { get; }
    public List<SourceRow> Items { get; }

    /// <summary>Group by kind, in the enum's own order — which puts the eager instruction chain
    /// first and the never-loaded settings files last.</summary>
    internal static List<SourceGroup> Build(List<ContextSource> sources, RowStyle style, SourceSort sort,
        UsageEvidence? evidence, DateTime nowUtc, HashSet<string> selected)
        => sources
            .GroupBy(s => s.Kind)
            .OrderBy(g => (int)g.Key)
            .Select(g => new SourceGroup(g.Key, g.ToList(), style, sort, evidence, nowUtc, selected))
            .ToList();
}

/// <summary>One measured file in the detail table.</summary>
public sealed class SourceRow
{
    /// <summary>A file nobody has touched in this long is worth a look — the info-level end of the
    /// review queue, and the one health signal available before the rule engine lands.</summary>
    private const int StaleDays = 90;

    internal SourceRow(ContextSource s, RowStyle style, UsageEvidence? evidence, DateTime nowUtc,
        HashSet<string> selected)
    {
        EagerTokens = s.EagerTokens;
        Label = s.Label;
        FullPath = s.Path;
        Mode = ContextText.Mode(s);
        Size = ContextText.Size(s.Bytes);
        Tokens = TokenEstimate.Format(s.Tokens);
        Eager = s.EagerTokens > 0 ? TokenEstimate.Format(s.EagerTokens) : "—";
        EagerWeight = s.EagerTokens > 0 ? "SemiBold" : "Normal";
        Modified = s.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd");

        bool indexed = s.Mode == LoadMode.Lazy && s.EagerTokens > 0;
        (ChipBackground, ChipForeground) = s.Mode switch
        {
            LoadMode.Eager => (style.EagerChip, style.ChipText),
            LoadMode.Lazy when indexed => (style.IndexChip, style.ChipText),
            LoadMode.Lazy => (style.LazyChip, style.LazyChipText),
            _ => (Brushes.Transparent, style.MutedText),
        };
        ChipTip = L.T(s.Mode switch
        {
            LoadMode.Eager => "context.mode.eagerTip",
            LoadMode.Lazy when indexed => "context.mode.indexTip",
            LoadMode.Lazy => "context.mode.lazyTip",
            _ => "context.mode.notLoadedTip",
        });

        // The health dot, from the two signals the scan already carries. The rule engine will have a
        // great deal more to say (severity per finding); this is the honest subset available now, and
        // an absent dot means "nothing to report", never "checked and healthy".
        int ageDays = (int)(DateTime.UtcNow - s.ModifiedUtc).TotalDays;
        Brush? dot = null;
        if (s.Note is { Length: > 0 } note) { dot = style.ProblemDot; DotTip = note; }
        else if (ageDays >= StaleDays) { dot = style.StaleDot; DotTip = L.T("context.health.stale", ageDays); }

        DotBrush = dot ?? Brushes.Transparent;
        DotVisibility = dot is null ? Visibility.Hidden : Visibility.Visible;

        // Evidence of use. Only skills and agents can carry it: a memory recall leaves no structured
        // trace, so a memory row stays blank rather than implying it is unused (see ContextUsage).
        // A row can be simulated away only if doing so would free eager context. For everything else
        // (a lazy memory body, a settings file) the honest answer is that removing it saves nothing
        // per session, so no checkbox is offered.
        Removable = s.EagerTokens > 0;
        SelectVisibility = Removable ? Visibility.Visible : Visibility.Collapsed;
        Selected = Removable && selected.Contains(s.Path);

        bool eligible = s.Kind is ContextKind.Skill or ContextKind.Agent;
        UsageStat? stat = eligible ? evidence?.For(s) : null;
        bool canReportZero = eligible && evidence?.CanReportZero(s) == true;

        Used = eligible ? ContextUsage.Format(stat, canReportZero) : "";
        UsedWeight = stat is { Total: > 0 } ? "SemiBold" : "Normal";
        UsedBrush = stat is { Total: > 0 } ? style.LazyChipText
            : canReportZero ? style.StaleDot
            : style.MutedText;
        UsedTip = !eligible ? null
            : stat is { Total: > 0 }
                ? L.T("context.usage.tip", stat.Total, evidence!.WindowDays, stat.Recent,
                    evidence.RecentDays, stat.LastUsedUtc.ToLocalTime().ToString("yyyy-MM-dd"))
                : canReportZero
                    ? L.T("context.usage.neverTip", evidence!.WindowDays)
                    : L.T("context.usage.unknownTip");
    }

    public string Label { get; }
    public string FullPath { get; }
    public string Mode { get; }
    public string Size { get; }
    public string Tokens { get; }
    public string Eager { get; }
    /// <summary>Bold the eager column only where there is a cost — the row's whole point.</summary>
    public string EagerWeight { get; }
    public string Modified { get; }

    public Brush ChipBackground { get; }
    public Brush ChipForeground { get; }
    public string ChipTip { get; }

    /// <summary>Eager tokens this row would give back if it were gone.</summary>
    internal int EagerTokens { get; }
    /// <summary>Whether removing it would free eager context at all.</summary>
    internal bool Removable { get; }
    public Visibility SelectVisibility { get; }
    /// <summary>Bound once at construction; the selection set stays the source of truth afterwards.</summary>
    public bool Selected { get; }

    /// <summary>"12×", the localized "never", or "" / "—" when there is nothing honest to say.</summary>
    public string Used { get; }
    public string UsedWeight { get; }
    public Brush UsedBrush { get; }
    public string? UsedTip { get; }

    public Brush DotBrush { get; }
    /// <summary><see cref="Visibility.Hidden"/>, not Collapsed — the gutter keeps its width so every
    /// label in the table starts at the same x.</summary>
    public Visibility DotVisibility { get; }
    public string? DotTip { get; }
}
