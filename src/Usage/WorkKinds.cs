namespace ClaudeTray;

/// <summary>One line of the by-kind table: what the group is, how many tasks it holds, and what they
/// cost. <paramref name="Median"/> is the middle task rather than the mean, because one overnight run
/// among forty short ones moves a mean and says nothing about a usual task.</summary>
internal readonly record struct WorkGroup(string Kind, string Name, int Tasks, long Median, long Total)
{
    /// <summary>A slash command is named; every other kind is only its kind.</summary>
    public bool IsCommand => Kind == nameof(TaskKind.Command) && Name.Length > 0;
}

/// <summary>
/// What kind of work ate the range (T333).
///
/// <para>The projects strip attributes spend to a <b>repo</b>, which answers <em>which project is
/// eating the week</em>. The sharper question is <em>which kind of work</em>, and a repo-level
/// breakdown cannot see it, because the expensive command and the cheap prompt land in the same repo.
/// Measured across 1,864 tasks when this was designed: 443 slash-command tasks carried <b>57%</b> of
/// all spend, at a median of $9.55 against $1.69 for a typed prompt, and a single command accounted
/// for every one of the 443.</para>
///
/// <para><b>A command's name is a name.</b> It stands with the model id, the tool names and the skill
/// names §I.1 already permits. The prompt that follows it is not a name and is not read here — the
/// index stores the command's own name and nothing of what was asked of it.</para>
///
/// <para><b>It reports and does not advise.</b> The table says a command was 57% of the range; it does
/// not say run fewer of them, for the same reason the projection verdict never says work less. What a
/// reader does with a number about their own week is theirs.</para>
/// </summary>
internal static class WorkKinds
{
    /// <summary>
    /// The table, heaviest first: one row per task kind, and one row per <em>named</em> command rather
    /// than a single lump for all of them.
    /// </summary>
    /// <remarks>Commands are split by name because the finding that produced this block was about one
    /// command, not about commands in general — rolled together they would have read as "automation is
    /// expensive", which is true of exactly one entry here and useless as a reading.</remarks>
    public static IReadOnlyList<WorkGroup> Of(IEnumerable<TaskRow> tasks)
    {
        var buckets = new Dictionary<(string Kind, string Name), List<long>>();
        foreach (TaskRow t in tasks)
        {
            if (t.Tokens <= 0) continue;   // a task that got no answer is not a size
            var key = (t.Kind, t.Kind == nameof(TaskKind.Command) ? t.Name : "");
            if (!buckets.TryGetValue(key, out List<long>? list)) buckets[key] = list = new List<long>();
            list.Add(t.Tokens);
        }

        return buckets.Select(kv =>
                   {
                       long[] sizes = kv.Value.Order().ToArray();
                       return new WorkGroup(kv.Key.Kind, kv.Key.Name, sizes.Length,
                                            sizes[(sizes.Length - 1) / 2], sizes.Sum());
                   })
                   .OrderByDescending(g => g.Total)
                   .ThenBy(g => g.Name, StringComparer.Ordinal)
                   .ToList();
    }

    /// <summary>What the whole table sums to — the denominator every share is taken against, computed
    /// from the same rows rather than from a token figure gathered elsewhere, so a share can never be
    /// of something the table does not show.</summary>
    public static long Total(IEnumerable<WorkGroup> groups) => groups.Sum(g => g.Total);

    /// <summary>The group's own name for a reader: a command as the user typed it, and every other
    /// kind through the string table so it is legible in five languages.</summary>
    public static string Label(WorkGroup g)
    {
        if (g.IsCommand) return g.Name;
        // Assembled into a variable rather than concatenated inside the call: a key built at run time
        // is one the key check cannot hold up either way, but a fragment sitting where a key belongs
        // reads to it as a key no table has — which is exactly what it reported (T314).
        string key = "stats.kind." + char.ToLowerInvariant(g.Kind[0]) + g.Kind[1..];
        return L.T(key);
    }
}
