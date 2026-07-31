using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// WinForms and WPF each contribute a Brush / Color / Orientation / HorizontalAlignment; pin these
// names to the WPF ones the gauge is drawn with (same convention as StatisticsWindow).
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace ClaudeTray;

/// <summary>
/// One row of the project list. Public (like the two view models below) because WPF resolves a
/// <c>{Binding}</c> path by reflection over a public type — an internal one binds to nothing, and
/// silently.
/// </summary>
public sealed class ProjectRow
{
    /// <param name="project">The project, or <c>null</c> for the "All projects" row that leads the list.</param>
    /// <param name="debt">Its context-debt grade, or null for the overview row.</param>
    /// <param name="style">Theme-resolved brushes, for the grade chip.</param>
    internal ProjectRow(ContextScan scan, ContextProject? project, ContextDebt? debt = null,
        RowStyle? style = null)
    {
        Scan = scan;
        Project = project;
        Debt = debt;
        GradeVisibility = debt is null ? Visibility.Collapsed : Visibility.Visible;
        GradeBrush = debt is null || style is null ? Brushes.Transparent : style.GradeChip(debt.Grade);
        GradeTip = debt is null
            ? null
            : L.T("context.grade.tip", debt.Grade, TokenEstimate.Format(debt.EagerTokens),
                debt.Findings, debt.High, debt.Medium, debt.Low);
        Estimated = project is null ? 0 : scan.EstimatedSessionZero(project);
    }

    internal ContextScan Scan { get; }
    internal ContextProject? Project { get; }
    internal ContextDebt? Debt { get; }

    public string Grade => Debt?.Grade.ToString() ?? "";
    public Visibility GradeVisibility { get; }
    public Brush GradeBrush { get; }
    public string? GradeTip { get; }
    /// <summary>True for the cross-project overview row.</summary>
    internal bool IsAll => Project is null;
    /// <summary>Session zero as the filesystem sees it: shared eager + this project's eager.</summary>
    internal int Estimated { get; }
    internal string Slug => Project?.Slug ?? "";

    public string Title => IsAll ? L.T("context.all.title") : Project!.ShortPath;

    /// <summary>
    /// What a session in this project costs. Blank for the overview row on purpose: this column means
    /// "per session", and there is no such thing for all projects at once — the shared part would be
    /// counted 33 times. The overview's own pane carries the three honest machine-wide numbers.
    /// </summary>
    public string Eager => IsAll ? "" : TokenEstimate.Format(Estimated);

    /// <summary>"18 sources · 6 memories", with the path state appended when it isn't a live directory.</summary>
    public string Detail
    {
        get
        {
            // Size rather than a file count: the footer already shows how many files were *walked*
            // (transcripts included), and two different file counts side by side read as a bug.
            if (IsAll)
                return L.T("context.all.rowDetail", Scan.Projects.Count,
                    ContextText.Size(Scan.Shared.Sum(s => s.Bytes) + Scan.Projects.Sum(p => p.Bytes)));

            int memories = Project!.Sources.Count(s =>
                s.Kind is ContextKind.MemoryFile or ContextKind.MemoryIndex);
            string detail = L.T("context.project.detail", Project.Sources.Count, memories);
            return Project.State switch
            {
                PathState.Missing => detail + " · " + L.T("context.state.missing"),
                PathState.NotAPath => detail + " · " + L.T("context.state.notAPath"),
                _ => detail,
            };
        }
    }

    /// <summary>The project's real directory, or why there isn't one.</summary>
    internal string PathLine
    {
        get
        {
            if (Project!.Path.Length == 0) return L.T("context.state.notAPath");
            return Project.State == PathState.Missing
                ? Project.Path + "  ·  " + L.T("context.state.missing")
                : Project.Path;
        }
    }

    /// <summary>Whether this row is the one named by a slug, directory name or full path. Never the
    /// overview row — a name on the command line always means a project.</summary>
    internal bool Matches(string? name) =>
        !IsAll && name is { Length: > 0 } &&
        (Project!.Slug.Equals(name, StringComparison.OrdinalIgnoreCase) ||
         Project.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
         Project.ShortPath.Equals(name, StringComparison.OrdinalIgnoreCase) ||
         Project.Path.Equals(name, StringComparison.OrdinalIgnoreCase));
}
