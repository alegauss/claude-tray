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

/// <summary>The display words shared by the window's view models.</summary>
internal static class ContextText
{
    public static string Kind(ContextKind kind) => L.T(kind switch
    {
        ContextKind.UserInstructions => "context.kind.userInstructions",
        ContextKind.ProjectInstructions => "context.kind.projectInstructions",
        ContextKind.NestedInstructions => "context.kind.nestedInstructions",
        ContextKind.Import => "context.kind.import",
        ContextKind.MemoryIndex => "context.kind.memoryIndex",
        ContextKind.MemoryFile => "context.kind.memoryFile",
        ContextKind.Skill => "context.kind.skill",
        ContextKind.Agent => "context.kind.agent",
        _ => "context.kind.settings",
    });

    /// <summary>
    /// Eager / lazy / index / not loaded. "index" is the honest word for a skill or agent: the body
    /// is only read when it is invoked, but its name and description sit in the always-loaded index,
    /// so having it available is never quite free.
    /// </summary>
    public static string Mode(ContextSource s) => L.T(s.Mode switch
    {
        LoadMode.Eager => "context.mode.eager",
        LoadMode.Lazy => s.EagerTokens > 0 ? "context.mode.index" : "context.mode.lazy",
        _ => "context.mode.notLoaded",
    });

    /// <summary>Bytes / KB / MB — MB because a whole machine's footprint is megabytes, and "1434 KB"
    /// is a number nobody reads.</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => L.T("context.size.bytes", bytes),
        < 1024 * 1024 => L.T("context.size.kb", (bytes / 1024.0).ToString("0.#", L.Culture)),
        _ => L.T("context.size.mb", (bytes / (1024.0 * 1024)).ToString("0.#", L.Culture)),
    };
}
