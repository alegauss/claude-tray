using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

// This is a WinForms + WPF hybrid, so System.Drawing and System.Windows.Media both contribute a
// Brush / Color / Point / Size. Pin these names to the WPF (Media) types the charts are drawn with.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ClaudeTray;

/// <summary>Part of <see cref="StatisticsPage"/> — split out by T133, moved verbatim.</summary>
internal partial class StatisticsPage
{
    /// <summary>
    /// A number as this window writes it: the caller picks the shape, <see cref="Fmt"/> picks the
    /// conventions — and this is the <b>only</b> place that names a culture for a number, so there is
    /// nothing left to bypass (T167).
    /// </summary>
    /// <remarks>
    /// The rest of this file was already invariant. The method note's five interpolations were bare
    /// <c>$"{value:0.#}"</c>, which formats with <c>CurrentCulture</c>: on a pt-BR machine run with
    /// <c>--lang en</c> the English popup read <em>"4,7 weeks of local transcripts"</em> eight lines
    /// above <c>≈ 1,319 tok/s</c> and <c>40%</c>. One window, two number conventions — and it was sitting
    /// in both verification screenshots of T159 and T163 without being seen.
    /// <para><b>Invariant is a decision, not the status quo winning.</b> A decimal comma inside a
    /// Portuguese sentence is what a Portuguese reader expects, and choosing it would not be wrong. It is
    /// refused because the app has no per-language numeric culture at all — <see cref="DateFmt"/> is
    /// <c>L.Culture</c> and formats dates and nothing else — and because a published screenshot has to
    /// mean the same thing on any machine it was taken on. Changing that rule is now an edit to one line
    /// here, which is the other half of the point.</para>
    /// </remarks>
    internal static string Num(double n, string format = "0.#") => n.ToString(format, Fmt);

    // "72%" — no space, matching the tray's percentage style.
    private static string Pct(double frac) => Num(Math.Round(Math.Clamp(frac, 0, 1) * 100), "0") + "%";

    // A token rate: whole numbers once it's fast, a decimal or two when it's a trickle.
    private static string Rate(double tps) =>
        tps >= 100 ? Num(tps, "#,##0")
        : tps >= 10 ? Num(tps, "0.0")
        : Num(tps, "0.00");

    // The live strip's axis (T110): a mark's value, and the hover that says what full height means.
    // "20k/s" rather than Big()'s "20.0k" — a scale label is read at a glance, and a trailing .0 on a
    // round ceiling is noise. Tokens in one second *are* tokens/second, so the unit is honest.
    private static string AxisTick(double tokens) => L.T("stats.live.axis", Tick(tokens));

    // What full height means. When columns run past it (T112) the hover has to say so out loud: how
    // many were cut, and what the busiest second actually was — a clipped chart that doesn't admit it
    // is the one thing worse than a flattened one.
    private static string ScaleTip(double ceiling, int clipped, double peak) =>
        clipped <= 0
            ? L.T("stats.live.scaleTip", Tick(ceiling))
            : L.T(clipped == 1 ? "stats.live.scaleTipClipped1" : "stats.live.scaleTipClippedN",
                  Tick(ceiling), clipped, Tick(peak));

    private static string Tick(double n) =>
        n >= 1_000_000 ? Num(n / 1e6, "0.##") + "M"
        : n >= 1_000 ? Num(n / 1e3, "0.##") + "k"
        : Num(n, "0.##");

    // Compact token count: 3.1M / 42k / 517.
    private static string Big(long n) =>
        n >= 1_000_000 ? Num(n / 1e6, "0.0") + "M"
        : n >= 1_000 ? Num(n / 1e3, "0.0") + "k"
        : Num(n, "0");

    // Dark theme when the primary text ink is light — used to pick the categorical bar hues (the
    // palette has a light and a dark step per series; the theme brushes only cover text/surfaces).
    private bool IsDarkTheme()
    {
        if (FindResource("TextFillColorPrimaryBrush") is SolidColorBrush b)
            return 0.299 * b.Color.R + 0.587 * b.Color.G + 0.114 * b.Color.B > 128;
        return true;
    }

    private static string LocalTime(double unix)
    {
        if (unix <= 0) return "—";
        DateTime local = DateTimeOffset.FromUnixTimeSeconds((long)unix).LocalDateTime;
        return local.ToString("MMM d, HH:mm", DateFmt);
    }

    // Clock time for a label drawn inside the plot, where space is tight: just "18:40" in a window that
    // can't span days (the 5-hour session), with the date prefixed on the multi-day weekly chart — same
    // "d/M" form as its day dividers — where the time alone wouldn't say which day.
    private static string ShortTime(double unix, double windowSeconds)
    {
        if (unix <= 0) return "—";
        DateTime local = DateTimeOffset.FromUnixTimeSeconds((long)unix).LocalDateTime;
        return windowSeconds >= 2 * 86400
            ? local.ToString("d/M HH:mm", DateFmt)
            : local.ToString("HH:mm", DateFmt);
    }

    // Compact duration, matching the tray tooltip's style: "2d 4h", "3h 20m", "45m", "now".
    private static string Dur(double seconds)
    {
        if (double.IsInfinity(seconds) || seconds <= 0) return seconds <= 0 ? L.T("dur.now") : "—";
        int s = (int)Math.Round(seconds);
        int d = s / 86400, h = s % 86400 / 3600, m = s % 3600 / 60;
        if (d > 0) return $"{Num(d, "0")}d {Num(h, "0")}h";
        if (h > 0) return $"{Num(h, "0")}h {Num(m, "00")}m";
        return $"{Num(Math.Max(1, m), "0")}m";
    }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
