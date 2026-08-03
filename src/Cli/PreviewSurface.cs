using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

// Both UI stacks are referenced (see AGENTS.md), so the bare name is ambiguous with its
// System.Drawing twin. This file is about WPF layout.
using Point = System.Windows.Point;

namespace ClaudeTray;

/// <summary>
/// What a preview tells the capture script it drew (T217).
///
/// <para><b>The failure this exists for.</b> Verifying T170 cost three captures and a full-screen grab
/// before the cause was visible, and <em>none of the three failed</em>: the script reported success and
/// named the right window each time, and the PNG simply did not contain the note the flag exists to
/// show. T199 taught the script to verify <em>whose</em> window it copied, which is why the failure was
/// silent rather than wrong — it honestly reported a correct copy of a window with no popup in it.
/// Nothing was checking that the capture contained the surface it was taken for, and nothing could:
/// only the app knows what it drew and where.</para>
///
/// <para>So the app says so, on stdout, in a line the script reads and asserts against the rectangle it
/// is about to copy. A flag that names a surface now proves that surface is in the pixels.</para>
/// </summary>
internal static class PreviewSurface
{
    /// <summary>The line the script matches. Deliberately dull and machine-first: a name, then the
    /// rectangle in physical screen pixels, which is the space <c>GetWindowRect</c> and
    /// <c>CopyFromScreen</c> already work in.</summary>
    internal const string Prefix = "preview-surface:";

    /// <summary>
    /// Announce a rendered surface once it has a size and a place on screen.
    ///
    /// <para>Reported in <b>physical</b> pixels: WPF lays out in DIPs and the script copies pixels, so a
    /// rectangle handed over in the wrong one is right at 100% and wrong at every scaling a developer
    /// here actually runs. <see cref="Visual.PointToScreen"/> already answers in device pixels; the size
    /// has to be scaled by the same transform.</para>
    /// </summary>
    internal static void Report(string name, FrameworkElement element)
    {
        try
        {
            if (PresentationSource.FromVisual(element) is not { } source) return;
            Matrix m = source.CompositionTarget.TransformToDevice;
            Point topLeft = element.PointToScreen(new Point(0, 0));
            int w = (int)Math.Round(element.ActualWidth * m.M11);
            int h = (int)Math.Round(element.ActualHeight * m.M22);
            if (w <= 0 || h <= 0) return;
            Console.WriteLine($"{Prefix} {name} {(int)Math.Round(topLeft.X)} {(int)Math.Round(topLeft.Y)} {w} {h}");
        }
        catch { /* a preview that cannot report is caught by the script's own "never reported" arm */ }
    }

    /// <summary>
    /// Hold every <see cref="Popup"/> under a preview window open, by construction.
    ///
    /// <para><c>StaysOpen="False"</c> is right for a person — click anywhere and the note goes away — and
    /// fatal for a capture: the script brings the window to the foreground, the popup loses mouse capture
    /// and is gone before the copy. T170 fixed that on the one popup it needed, at the call site, which
    /// left the next popup preview to rediscover it. A preview has no hand to click with, so the rule
    /// belongs to the preview host rather than to whichever page happens to own a popup.</para>
    ///
    /// <para>Walks the logical tree, not the visual one: a closed popup's child is not in the visual tree
    /// at all, which is exactly the state this needs to reach it in.</para>
    /// </summary>
    internal static void HoldPopupsOpen(DependencyObject root)
    {
        if (root is Popup popup) popup.StaysOpen = true;
        foreach (object? child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject node) HoldPopupsOpen(node);
    }
}
