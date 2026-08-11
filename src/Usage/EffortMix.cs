namespace ClaudeTray;

/// <summary>
/// What effort levels a session or a task ran at, and how many calls at each (T331).
///
/// <para><b>A mix, not a majority.</b> A session that ran 40 calls at <c>high</c> and 10 at
/// <c>xhigh</c> is not an <c>xhigh</c> session and it is not a <c>high</c> one either — and the
/// minority is the expensive part, so a vote that rounded it away would hide exactly the thing worth
/// seeing. Every level that ran is kept with its call count, ordered by the ladder rather than by
/// size, so two rows are comparable at a glance.</para>
///
/// <para><b>Why the ladder is spelled here and nowhere else.</b> The levels are an ordered scale and
/// the order is not alphabetical — <c>xhigh</c> sits between <c>high</c> and <c>max</c>, which no
/// sort would guess. Naming it once means a level this machine has never run is still rendered in
/// its right place the first time it appears: measured over this repository's transcripts, only
/// <c>high</c> (127,292 calls) and <c>xhigh</c> (6,254) have ever been seen, so <c>low</c>,
/// <c>medium</c> and <c>max</c> are all first sightings waiting to happen.</para>
/// </summary>
internal static class EffortMix
{
    /// <summary>The scale, weakest first. An effort the transcript names that is not on this ladder is
    /// still counted and still shown — it sorts after the known levels rather than being dropped,
    /// because a level this app has not heard of is news, not noise.</summary>
    public static readonly string[] Ladder = { "low", "medium", "high", "xhigh", "max" };

    /// <summary>Where <paramref name="name"/> sits on the ladder; unknown levels sort last.</summary>
    public static int Rank(string name)
    {
        int i = Array.IndexOf(Ladder, name);
        return i < 0 ? Ladder.Length : i;
    }

    /// <summary>Fold <paramref name="add"/> into <paramref name="into"/>, call counts summed.</summary>
    public static void Merge(Dictionary<string, int> into, IReadOnlyDictionary<string, int>? add)
    {
        if (add is null) return;
        foreach (KeyValuePair<string, int> kv in add)
            into[kv.Key] = into.TryGetValue(kv.Key, out int had) ? had + kv.Value : kv.Value;
    }

    /// <summary>The levels that ran, weakest first, with their call counts.</summary>
    public static (string Name, int Calls)[] Ordered(IReadOnlyDictionary<string, int>? mix) =>
        mix is null || mix.Count == 0
            ? Array.Empty<(string, int)>()
            : mix.Where(kv => kv.Value > 0)
                 .OrderBy(kv => Rank(kv.Key))
                 .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                 .Select(kv => (kv.Key, kv.Value))
                 .ToArray();

    /// <summary>
    /// The mix as one line: a single level is its own name, and a mix names every level with its share
    /// of the calls. Empty where nothing named an effort, so the column stays blank rather than
    /// asserting a level for turns that carried none.
    /// </summary>
    /// <remarks>Shares rather than raw counts: the call count is already its own column beside this
    /// one, and what a reader compares across rows is the proportion, not the absolute.</remarks>
    public static string Line(IReadOnlyDictionary<string, int>? mix)
    {
        (string Name, int Calls)[] parts = Ordered(mix);
        if (parts.Length == 0) return "";
        if (parts.Length == 1) return Label(parts[0].Name);

        int total = parts.Sum(p => p.Calls);
        return string.Join("  ·  ", parts.Select(p =>
            $"{Label(p.Name)} {Nums.Of((double)p.Calls * 100 / total, "0")}%"));
    }

    /// <summary>The level's own name, translated where this app knows it. An unknown level is printed
    /// as the transcript spelled it — the honest rendering of news.</summary>
    public static string Label(string name)
    {
        string key = "stats.effort." + name;
        string s = L.T(key);
        return s == key ? name : s;
    }
}
