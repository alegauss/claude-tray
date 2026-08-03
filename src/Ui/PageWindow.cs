using System.Windows;

namespace ClaudeTray;

/// <summary>
/// A bare window around one page, for the previews only (<c>--settings</c>, <c>--stats</c>,
/// <c>--context --window</c> and the two capture flags).
///
/// <para>The app itself shows every page inside <see cref="MainWindow"/>. The previews deliberately do
/// not: what they exist for is one page rendered deterministically — the published screenshots and the
/// off-screen <c>RenderTargetBitmap</c> captures — and putting the shell's nav strip in every one of
/// them would add chrome to images that are about the page. <c>--main</c> opens the real shell when
/// the shell is what is being looked at.</para>
///
/// <para>Code-only (no XAML) because it has nothing to lay out: it carries the chrome a page cannot —
/// title, size, theme and the app icon — and hosts the page as its content.</para>
/// </summary>
internal sealed class PageWindow : Window
{
    internal PageWindow(UIElement page, string title, double width, double height,
                        double minWidth = 0, double minHeight = 0)
    {
        Title = title;
        Width = width;
        Height = height;
        MinWidth = minWidth > 0 ? minWidth : width * 0.9;
        MinHeight = minHeight > 0 ? minHeight : height * 0.8;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ThemeMode = ThemeMode.System;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
        Content = page;

        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath));
        }
        catch { /* fall back to the default window icon */ }

        // Every popup under a preview stays open, by construction (T217). This host is the one thing all
        // the preview flags share, and a rule kept at one page's call site is a rule the next popup
        // preview gets to rediscover — which is how three captures of the method note came back correct,
        // green, and empty. Re-run on Loaded too: a page builds its popups as it is laid out.
        Loaded += (_, _) => PreviewSurface.HoldPopupsOpen(this);
    }
}
