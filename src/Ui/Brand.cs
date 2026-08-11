// This is a WinForms + WPF hybrid, so System.Drawing and System.Windows.Media both contribute a Brush and
// a Color. Pin these two names to the WPF (Media) types; the GDI+ one below is spelled in full, which is
// the point of this file — the two edges are named, not guessed at.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ClaudeTray;

/// <summary>
/// The colours whose value is not a free choice, declared once (T310).
///
/// <para><b>Clay</b> means one thing: <em>past the quota included in your plan</em>. T182 split that state
/// off from "stopped", T183 gave it a series, T184 a toast and T188 the icon's own tile, and each of them
/// turned on it meaning that in one place and nothing else meaning it anywhere. It was nonetheless written
/// out twelve times across five files — twice from bytes in <see cref="IconRenderer"/>, twice more in
/// <c>StatisticsPage</c>'s code-behind, once as a string in <c>ToastWindow</c>'s palette, and seven times
/// as literal hex in markup. One of those twelve was pinned by a check; eleven could move without a red
/// build, and the failure mode is quiet: a tray icon and a chart band in two clays, saying two things where
/// the whole point was one.</para>
///
/// <para><b>Why a value and not a brush.</b> The tray icon is GDI+ and wants a
/// <see cref="System.Drawing.Color"/>; the charts are WPF and want a frozen <see cref="Brush"/>; the markup
/// wants something <c>x:Static</c> can reach; and the toast palette is a table of strings. No one type
/// serves all four, so what is shared is the value and each edge converts — which is why the bytes, and not
/// any one of the four, are what the rest of this file is derived from.</para>
/// </summary>
internal static class Brand
{
    /// <summary>Clay, as the three bytes everything else here is built from.</summary>
    public const byte ClayR = 0xD9;
    public const byte ClayG = 0x77;
    public const byte ClayB = 0x57;

    /// <summary>How opaque the over-quota band is where it shades a stretch of chart behind the curve —
    /// enough to read as a region, light enough that the line over it stays the thing being read.</summary>
    public const byte BandAlpha = 0x3D;

    public static readonly Color Clay = Color.FromRgb(ClayR, ClayG, ClayB);

    /// <summary>Frozen, like every other brush this app keeps: they are shared across threads and never
    /// mutated, and an unfrozen one pays a lock on every draw.</summary>
    public static readonly Brush ClayBrush = Frozen(new SolidColorBrush(Clay));

    /// <summary>The same clay as the band behind a curve — one alpha, so the chart's shading and the
    /// legend swatch that explains it cannot drift apart.</summary>
    public static readonly Brush ClayBandBrush =
        Frozen(new SolidColorBrush(Color.FromArgb(BandAlpha, ClayR, ClayG, ClayB)));

    /// <summary>For GDI+, which draws the tray icon and knows nothing about WPF's types.</summary>
    public static System.Drawing.Color ClayGdi => System.Drawing.Color.FromArgb(ClayR, ClayG, ClayB);

    /// <summary>For the toast palette, which is a table of strings because that is what the gradient
    /// builder there takes.</summary>
    public static string ClayHex => $"#{ClayR:X2}{ClayG:X2}{ClayB:X2}";

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }
}
