using System.Globalization;

namespace ClaudeTray;

/// <summary>
/// The one invitation to star the repository, after a week of use (T410). Stars are what the GitHub page,
/// the awesome lists and a Show HN reader see first, and nobody who installs the app is otherwise told the
/// repository exists.
///
/// <para>A week is <see cref="Days"/> distinct <em>local</em> days the tray ran on, counted in the settings
/// file — <c>usage-history</c> keeps only eight days and a first-run date would count calendar time, not
/// use. Nothing leaves the machine to decide it. Shown once: the flag is set when the card is, so neither an
/// update, a profile switch nor the card timing out brings it back.</para>
/// </summary>
internal static class StarInvite
{
    public const int Days = 7;
    public const string RepositoryUrl = "https://github.com/alegauss/claude-tray";

    /// <summary>
    /// Count <paramref name="today"/> if it has not been counted, and say whether this is the moment to
    /// invite. Mutates <paramref name="s"/>; <c>Changed</c> says whether it needs saving. A clock set back
    /// to an earlier day counts nothing, so a day is never counted twice.
    /// </summary>
    public static (bool Changed, bool Show) Note(Settings s, DateTime today)
    {
        if (s.StarInvited) return (false, false);
        string day = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (string.CompareOrdinal(day, s.LastUsedDay) <= 0) return (false, false);

        s.LastUsedDay = day;
        s.DaysUsed++;
        if (s.DaysUsed < Days) return (true, false);
        s.StarInvited = true;
        return (true, true);
    }

    /// <summary>The card, the same for the tray and the preview: no bar, since there is no quantity.</summary>
    public static ToastWindow Card(Action openRepository) =>
        new ToastWindow("⭐", L.T("toast.star.title"), L.T("toast.star.subtitle"), null, null,
                        L.T("toast.star.caption"), "", ToastWindow.ToastTheme.Star)
            .WithActions(L.T("toast.star.open"), L.T("toast.star.later"), openRepository);
}
