using System.Windows;
using System.Windows.Controls;

// Both UI stacks are referenced (AGENTS.md), so these bare names are ambiguous with their WinForms and
// System.Drawing twins. This file is about WPF layout, like PreviewSurface next door.
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Panel = System.Windows.Controls.Panel;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using PixelFormats = System.Windows.Media.PixelFormats;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace ClaudeTray;

/// <summary>
/// Part of <see cref="SettingsPage"/> — the off-screen capture and what it can say about what it framed.
/// </summary>
internal partial class SettingsPage
{
    /// <summary>
    /// Where a named element sits in its page's scroller, and whether the viewport can hold it (T375).
    ///
    /// <para><b>Why a capture has to answer this.</b> <c>scroll=&lt;dip&gt;</c> is a number nothing knows
    /// the right value of: framing one card cost five captures at 1120, 1140, 1152, 1290 and 1440, three of
    /// them read and thrown away. Worse, the longest panel's last card sits near the scroll clamp, where a
    /// number that is too large lands quietly at the bottom instead of failing. The caller knows which
    /// element it wants photographed and only the app knows where that is.</para>
    ///
    /// <para>And the second half, which is the one that would have saved the other four captures: an
    /// element taller than the viewport <em>cannot</em> be photographed whole, and a picture that ends
    /// mid-row looks exactly like a bug. Said out loud, once, on the first run.</para>
    /// </summary>
    internal readonly record struct Framing(string Card, double Offset, double Height, double Viewport)
    {
        /// <summary>Half a dip of slack, because a card laid out to exactly the viewport height comes back
        /// off the layout pass as 712.0000001 often enough to matter.</summary>
        public bool Fits => Height <= Viewport + 0.5;

        /// <summary>The line a caller reads. Dull and machine-first, on
        /// <see cref="PreviewSurface"/>'s rule: the app states what it did, the script or the person
        /// decides what to do about it.</summary>
        public string Line =>
            $"capture-frame: {Card} scroll={Offset:0} element={Height:0}dip viewport={Viewport:0}dip "
            + (Fits ? "WHOLE" : $"PARTIAL {Nums.Pct(Viewport / Height)} of it");
    }

    /// <summary>
    /// Resolve <c>card=&lt;x:Name&gt;</c> against the page that is showing: the scroll offset that brings
    /// that element's top into view, and the framing to report. The refusal is the second half of the
    /// tuple and is non-null exactly when nothing was resolved — the caller must print it and write no
    /// file, on the rule every naming flag here follows (T262): a name this build does not have must not
    /// render something else into the path the caller gave.
    /// </summary>
    internal (double Offset, Framing Frame, string? Refusal) FrameFor(string card)
    {
        UpdateLayout();
        if (VisiblePageScroller() is not { } sv)
            return (0, default, "this page has no scroller, so there is nothing to frame against");

        // Resolved against every named element, listed as only the notable ones. A section header is a
        // 20dip TextBlock and is exactly what a caller often wants at the top of a picture, so the floor
        // must not decide what can be *asked for* — only what a refusal is worth printing.
        FrameworkElement? found = FrameTargets(sv, floor: 0).FirstOrDefault(
            e => string.Equals(e.Name, card, StringComparison.Ordinal));
        if (found is null)
        {
            List<FrameworkElement> notable = FrameTargets(sv, NotableDip);
            return (0, default, $"no element named '{card}' on this page. Refusing rather than capturing "
                                + "whatever the default scroll shows, which is how a screenshot of the "
                                + "wrong thing gets published (T262). The frames worth naming here, top "
                                + $"first (any x:Name on the page also works):{Environment.NewLine}  "
                                + string.Join(Environment.NewLine + "  ",
                                    notable.Select(e => $"{e.Name,-24} {e.ActualHeight:0}dip")));
        }

        // Relative to the scrolled content, plus the current offset: TransformToAncestor answers in the
        // scroller's own space, which already has the scroll applied to it.
        double y;
        try { y = found.TransformToAncestor(sv).Transform(new Point(0, 0)).Y + sv.VerticalOffset; }
        catch { return (0, default, $"'{card}' is on this page but not laid out, so it has no position yet"); }

        // A little air above the element rather than flush against the edge, and never a negative offset —
        // ScrollToVerticalOffset clamps, but a reported number that the caller cannot reproduce is worse
        // than a clamped one.
        double offset = Math.Max(0, y - FrameMargin);
        return (offset, new Framing(found.Name, offset, found.ActualHeight, sv.ViewportHeight), null);
    }

    /// <summary>Breathing room above a framed element, in dips. Small enough that the element is still at
    /// the top of the picture and large enough that it is not touching the edge.</summary>
    private const double FrameMargin = 12;

    /// <summary>How tall a named element has to be to be listed in a refusal. This page names dozens of
    /// value labels and hint lines, and a catalogue listing all of them is one nobody reads — so the list
    /// is cards and rows, and anything smaller stays askable but unlisted.</summary>
    private const double NotableDip = 32;

    /// <summary>
    /// Every element the <em>page's own markup</em> named, inside the visible scroller and at or above
    /// <paramref name="floor"/> dips, in the order they appear down the page.
    ///
    /// <para><b>The page's markup, not the visual tree's names.</b> A first pass listed
    /// <c>PART_ScrollContentPresenter</c>, <c>ContentBorder</c>, <c>Desc</c> and <c>LayoutGrid</c> —
    /// framework template parts, named by a <c>ControlTemplate</c> and not by anything in this
    /// repository, four of them repeated. <c>TemplatedParent</c> is what separates them, which is the
    /// same rule <see cref="PreviewSurface"/>'s neighbour states for the row sweep: exclude them by
    /// <em>what they are</em> rather than by a list of ids that the next control's template defeats.</para>
    /// </summary>
    private static List<FrameworkElement> FrameTargets(ScrollViewer sv, double floor)
    {
        var found = new List<(double Y, FrameworkElement Element)>();
        Walk(sv);
        return found.OrderBy(t => t.Y).Select(t => t.Element).ToList();

        void Walk(DependencyObject node)
        {
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(node, i);
                if (child is FrameworkElement { Name.Length: > 0, TemplatedParent: null } fe
                    && fe.ActualHeight >= floor)
                {
                    try { found.Add((fe.TransformToAncestor(sv).Transform(new Point(0, 0)).Y, fe)); }
                    catch { /* not laid out; it is not a frame anybody can point at either */ }
                }
                Walk(child);
            }
        }
    }

    /// <summary>
    /// Render the window's content to a PNG at 1.5×, off-screen, without depending on it being visible
    /// or foreground — the same deterministic path <c>StatisticsPage.SaveSnapshot</c> takes, and for
    /// the same reason: the screen-copy capture script grabs whatever pixels are *on screen* in the
    /// window's rectangle, so any app that steals focus or sits on top ends up in the file. Behind
    /// <c>--capture-settings</c>.
    /// </summary>
    /// <param name="scrollBy">Device-independent pixels to scroll the visible page's ScrollViewer down
    /// first, so a section below the fold can be captured without resizing the window. <c>card=</c>
    /// resolves to one of these through <see cref="FrameFor"/> rather than being guessed (T375).</param>
    internal void SaveSnapshot(string path, double scrollBy = 0)
    {
        UpdateLayout();
        if (scrollBy > 0 && VisiblePageScroller() is { } sv)
        {
            sv.ScrollToVerticalOffset(scrollBy);
            UpdateLayout();
        }
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

        var target = (FrameworkElement)Content;
        // The window's Mica backdrop isn't part of the visual tree, so paint an opaque themed surface
        // behind the content for the snapshot, then put it back.
        Brush? prev = (target as Panel)?.Background;
        if (target is Panel panel)
        {
            panel.Background = TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush
                               ?? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
            panel.UpdateLayout();
        }

        const double scale = 1.5;
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(target.ActualWidth * scale), (int)(target.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(target);

        if (target is Panel p2) p2.Background = prev;

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using FileStream fs = OutFile.Create(path);
        encoder.Save(fs);
    }

    /// <summary>The ScrollViewer of whichever page is currently shown, so a capture can scroll it.</summary>
    private ScrollViewer? VisiblePageScroller()
    {
        foreach (Grid pane in
                 new[] { GeneralPane, DisplayPane, ClaudeCodePane, NotificationsPane, SystemPane, AboutPane })
            if (pane.Visibility == Visibility.Visible)
                return pane.Children.OfType<ScrollViewer>().FirstOrDefault();
        return null;
    }
}
