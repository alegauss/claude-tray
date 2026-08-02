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

/// <summary>How the rows inside each group are ordered. Matches the order of the sort picker.</summary>
internal enum SourceSort
{
    /// <summary>What it costs every request, biggest first — the default, and the actionable one.</summary>
    Eager,
    /// <summary>Biggest file first, eager or not.</summary>
    Size,
    /// <summary>Oldest first — the review queue.</summary>
    Age,
}

/// <summary>
/// The theme-resolved brushes the rows are drawn with, looked up once per render instead of per row.
/// Tints are translucent so one pair of values works on both the light and the dark card surface.
/// </summary>
internal sealed record RowStyle(
    Brush EagerChip, Brush IndexChip, Brush LazyChip, Brush ChipText, Brush LazyChipText,
    Brush MutedText, Brush ProblemDot, Brush StaleDot, Brush GoodChip, Brush WarnChip, Brush BadChip)
{
    /// <summary>Green / amber / red tint for A-B, C-D and F. Three tones rather than five: the letter
    /// carries the detail, and five shades of one hue are not distinguishable anyway.</summary>
    public Brush GradeChip(DebtGrade grade) => grade switch
    {
        DebtGrade.A or DebtGrade.B => GoodChip,
        DebtGrade.C or DebtGrade.D => WarnChip,
        _ => BadChip,
    };

    public static RowStyle For(FrameworkElement host, bool dark)
    {
        Brush Tint(byte r, byte g, byte b) => Frozen(new SolidColorBrush(
            Color.FromArgb(dark ? (byte)0x40 : (byte)0x30, r, g, b)));

        return new RowStyle(
            // Blue for what is paid every request, green for the index-only cost of a skill/agent —
            // the same two hues the gauge uses for those sources, so the chip and the bar agree.
            EagerChip: Tint(0x39, 0x87, 0xE5),
            IndexChip: Tint(0x19, 0x9E, 0x70),
            LazyChip: (Brush)host.FindResource("SubtleFillColorSecondaryBrush"),
            ChipText: (Brush)host.FindResource("TextFillColorPrimaryBrush"),
            LazyChipText: (Brush)host.FindResource("TextFillColorSecondaryBrush"),
            MutedText: (Brush)host.FindResource("TextFillColorTertiaryBrush"),
            ProblemDot: Frozen(new SolidColorBrush(dark
                ? Color.FromRgb(0xE8, 0x6A, 0x5E)
                : Color.FromRgb(0xC4, 0x3E, 0x3E))),
            StaleDot: Frozen(new SolidColorBrush(dark
                ? Color.FromRgb(0xE0, 0xA0, 0x30)
                : Color.FromRgb(0xC7, 0x77, 0x00))),
            GoodChip: Tint(0x19, 0x9E, 0x70),
            WarnChip: Tint(0xE0, 0xA0, 0x30),
            BadChip: Tint(0xE3, 0x3B, 0x30));
    }

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }
}
