using System.Globalization;

namespace ClaudeTray;

/// <summary>
/// How this app writes a number, decided once (T216) — and the only place in it that names a culture
/// for one, so a new call site has somewhere to look instead of a neighbour to copy.
///
/// <para><b>The rule: numbers are invariant, dates follow the display language.</b> A figure is written
/// with a period decimal and no OS-derived grouping wherever it appears — chart axis, gauge caption,
/// toast, settings page, file size; a date or a clock time is written with <see cref="L.DateCulture"/>,
/// which is what makes "14 de nov." read as Portuguese. There is no third answer, and nothing here
/// varies with the machine's locale.</para>
///
/// <para><b>Why not a split.</b> T167 settled one window on invariant and, in settling it, showed the app
/// answered the question both ways: five surfaces formatted the same kind of figure with the display
/// language. The obvious repair was a stated split — invariant for anything read against a chart,
/// the display language for prose — and it does not survive contact with the surfaces it would govern.
/// One row of the Context page's file list carries a size and a token estimate side by side, and
/// <see cref="TokenEstimate.Format"/> cannot join a split's prose half: "≈4,9k" reads as a thousands
/// separator, which is the one misreading a token count cannot afford. The gauge caption puts a share
/// of the window — read against the bar directly under it — in the same sentence as a dollar cost.
/// Wherever the split's line is drawn it cuts through a sentence, which is the defect T167 removed from
/// the Statistics page, re-introduced by rule. A Portuguese reader has a real claim to a decimal comma
/// and choosing it would not be wrong; it is refused because the app cannot honour it in half a sentence,
/// and because a published screenshot has to mean the same thing on any machine it was taken on.</para>
///
/// <para><b>What holds it up.</b> <c>--selftest</c>'s <c>format</c> section runs every static formatter on
/// every surface that puts a number in a sentence — derived by reflection, never listed — twice, once
/// under a culture whose decimal separator is a comma, and fails on any that answers differently. That
/// is the defect this rule actually meets in the wild: a bare <c>$"{value:0.#}"</c>, which formats with
/// <see cref="CultureInfo.CurrentCulture"/> and is what T167 was. Dates are outside the sweep by
/// construction — they name <see cref="L.DateCulture"/>, and the sweep varies the OS culture, not the
/// display language.</para>
/// </summary>
internal static class Nums
{
    /// <summary>The one culture a number is written in. Nothing else in the app names one.</summary>
    internal static CultureInfo Fmt => CultureInfo.InvariantCulture;

    /// <summary>A number as this app writes it — the caller picks the shape, this picks the conventions.</summary>
    public static string Of(double n, string format = "0.#") => n.ToString(format, Fmt);

    /// <summary>
    /// A fraction as a percentage: "27%", no space — the tray's style, and the reason this exists rather
    /// than a <c>"P0"</c>, whose invariant form inserts one. Not clamped: a context load past its window
    /// is a real reading and rounding it down to 100% would hide it.
    /// </summary>
    public static string Pct(double frac) => Of(Math.Round(frac * 100), "0") + "%";
}
