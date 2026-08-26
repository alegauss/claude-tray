using System.Text.Json;

namespace ClaudeTray;

/// <summary>
/// What unioning two <c>settings.json</c> files would actually add, so the one decision the linking
/// script refuses to make for you can be made on evidence (T373).
///
/// <para><b>Why this exists.</b> T367 withheld <c>settings.json</c> for a good reason — a union widens the
/// other account's permission allowlist, and that is the user's call rather than a default — and then
/// left the call to be made blind: the script said "your decision" and named neither allowlist.
/// Everything needed to answer is in two files this app already opens, and the answer is short:
/// <b>these N entries would be added to the other side, and here they are.</b></para>
///
/// <para><b>Read, never merged.</b> Nothing here writes, and the verdict stays
/// <see cref="ProfileLink.Verdict.Withheld"/> — this only supplies the reading the commented-out command
/// can be uncommented on. Measuring permissions and hooks is exactly what §I.4 permits and editing them
/// is exactly what it forbids.</para>
///
/// <para><b>Three things it must keep apart</b>, because folding them together is how a report about risk
/// stops describing risk:</para>
/// <list type="bullet">
/// <item><b>Added entries, not a difference.</b> Two files differing is true of every pair and settles
/// nothing. Which rules arrive on each side is the whole reading.</item>
/// <item><b>Widening is not narrowing.</b> Entries arriving in <c>deny</c> take capability away, which is
/// safe in the direction that matters. A report saying "12 rules would be added" without splitting them
/// has made the safe half look like the risky one.</item>
/// <item><b>A hook is a command line that runs.</b> Adopting the other profile's hooks is a larger
/// decision than adopting a path rule, so it is reported beside the lists rather than counted into
/// them.</item>
/// </list>
///
/// <para><b>Unreadable is not zero.</b> A file that will not parse yields <see cref="Reading.Error"/> and
/// no counts at all, because a nought presented as a measurement is worse here than an absence: it reads
/// as "nothing would change" on the one surface whose job is saying what would.</para>
/// </summary>
internal static class SettingsUnion
{
    /// <summary>The permission lists a union touches, in the order they are reported: the two that grant
    /// first, then the one that constrains, then the one that widens filesystem reach.</summary>
    private static readonly (string Name, bool Narrows)[] Lists =
    {
        ("allow", false), ("ask", false), ("deny", true), ("additionalDirectories", false),
    };

    /// <summary>
    /// One list, and what a union would put into each side of it. Both directions, because a union does
    /// exactly that: the side keeping its files gains the other's entries, and the side reading through
    /// the link then gains everything.
    /// </summary>
    /// <param name="Narrows">This list takes capability away rather than granting it, so entries arriving
    /// in it are the safe direction and must not be counted beside the others.</param>
    public readonly record struct Widening(
        string List, string[] ToPrimary, string[] ToSecondary, bool Narrows)
    {
        public int Count => ToPrimary.Length + ToSecondary.Length;
    }

    /// <summary>
    /// One event with hooks on only one side. A hook is a shell command the harness runs, so this reports
    /// the count and the side rather than a total: "the other profile runs 2 commands on PreToolUse that
    /// this one does not" is a decision, and "3 hooks differ" is not.
    /// </summary>
    public readonly record struct HookGap(string Event, int OnPrimary, int OnSecondary);

    /// <summary>
    /// What a union of one settings file would do. <paramref name="Error"/> set means nothing was
    /// measured — never that nothing would change.
    /// </summary>
    public sealed record Reading(
        string Name, IReadOnlyList<Widening> Lists, IReadOnlyList<HookGap> Hooks, string? Error = null)
    {
        /// <summary>Entries that would <b>grant</b> something somewhere — the figure the decision is
        /// actually about, with the narrowing list left out of it on purpose.</summary>
        public int Granting => Lists.Where(w => !w.Narrows).Sum(w => w.Count);

        /// <summary>Entries arriving in a list that constrains, counted apart.</summary>
        public int Narrowing => Lists.Where(w => w.Narrows).Sum(w => w.Count);

        /// <summary>Whether there is anything at all to decide.</summary>
        public bool Empty => Error is null && Granting == 0 && Narrowing == 0 && Hooks.Count == 0;
    }

    /// <summary>
    /// Read both sides of one settings file and report what unioning them would add.
    /// <paramref name="name"/> is the file's own name, since the two this is asked about —
    /// <c>settings.json</c> and <c>settings.local.json</c> — are reported apart: one is shared and one is
    /// this machine's, and averaging them would answer neither question.
    /// </summary>
    public static Reading For(string primaryDir, string secondaryDir, string name)
    {
        if (Read(Path.Combine(primaryDir, name)) is not { } primary)
            return Absent(name, primaryDir, secondaryDir, onPrimary: true);
        if (Read(Path.Combine(secondaryDir, name)) is not { } secondary)
            return Absent(name, primaryDir, secondaryDir, onPrimary: false);

        var lists = new List<Widening>();
        foreach ((string list, bool narrows) in Lists)
        {
            string[] a = Strings(primary, list), b = Strings(secondary, list);
            string[] toPrimary = b.Except(a, StringComparer.Ordinal).ToArray();
            string[] toSecondary = a.Except(b, StringComparer.Ordinal).ToArray();
            if (toPrimary.Length + toSecondary.Length > 0)
                lists.Add(new Widening("permissions." + list, toPrimary, toSecondary, narrows));
        }

        return new Reading(name, lists, HookGaps(primary, secondary));
    }

    /// <summary>
    /// A file only one side has. Not an error and not "nothing to decide": adopting it wholesale is the
    /// widest form of this decision, so it is reported as such rather than as an empty union.
    /// </summary>
    private static Reading Absent(string name, string primaryDir, string secondaryDir, bool onPrimary)
    {
        string missing = onPrimary ? primaryDir : secondaryDir;
        string other = onPrimary ? secondaryDir : primaryDir;
        bool otherHasIt = File.Exists(Path.Combine(other, name));
        return new Reading(name, Array.Empty<Widening>(), Array.Empty<HookGap>(),
            otherHasIt
                ? $"only one of the two profiles has a {name}, so a link would hand its whole contents to "
                  + "the other rather than union anything - read that file before uncommenting"
                : $"neither profile has a {name}, so there is nothing to decide");
    }

    /// <summary>
    /// One settings file as a document, or null when it is absent or will not parse. Comments and trailing
    /// commas are tolerated because a hand-edited settings file has both, and a parse that refuses them
    /// would report "unreadable" for a file Claude Code itself is happily using.
    /// </summary>
    private static JsonDocument? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
            });
        }
        catch { return null; }
    }

    /// <summary>One permission list as strings, empty where the file does not carry it. Order is not
    /// meaningful in these lists, so this compares as a set and reports what arrives.</summary>
    private static string[] Strings(JsonDocument doc, string list)
    {
        try
        {
            if (!doc.RootElement.TryGetProperty("permissions", out JsonElement permissions)
                || permissions.ValueKind != JsonValueKind.Object
                || !permissions.TryGetProperty(list, out JsonElement array)
                || array.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            return array.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .Distinct(StringComparer.Ordinal).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Which hook events the two sides disagree about, with how many commands each has on it. Counted per
    /// event and never totalled: the question a person answers is "do I want the other profile's
    /// PreToolUse commands running in this one", and a single number cannot be answered.
    /// </summary>
    private static List<HookGap> HookGaps(JsonDocument primary, JsonDocument secondary)
    {
        Dictionary<string, int> a = HookCounts(primary), b = HookCounts(secondary);
        var gaps = new List<HookGap>();
        foreach (string ev in a.Keys.Union(b.Keys, StringComparer.Ordinal).OrderBy(e => e, StringComparer.Ordinal))
        {
            a.TryGetValue(ev, out int onPrimary);
            b.TryGetValue(ev, out int onSecondary);
            if (onPrimary != onSecondary) gaps.Add(new HookGap(ev, onPrimary, onSecondary));
        }
        return gaps;
    }

    /// <summary>How many hook commands each event carries. The shape is
    /// <c>hooks.&lt;Event&gt;[].hooks[]</c>, and anything that does not match it counts as nothing rather
    /// than throwing — a settings file this app cannot recognise is one it must not report about.</summary>
    private static Dictionary<string, int> HookCounts(JsonDocument doc)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            if (!doc.RootElement.TryGetProperty("hooks", out JsonElement hooks)
                || hooks.ValueKind != JsonValueKind.Object) return counts;
            foreach (JsonProperty ev in hooks.EnumerateObject())
            {
                if (ev.Value.ValueKind != JsonValueKind.Array) continue;
                int n = 0;
                foreach (JsonElement matcher in ev.Value.EnumerateArray())
                    n += matcher.ValueKind == JsonValueKind.Object
                         && matcher.TryGetProperty("hooks", out JsonElement inner)
                         && inner.ValueKind == JsonValueKind.Array
                        ? inner.GetArrayLength() : 0;
                if (n > 0) counts[ev.Name] = n;
            }
        }
        catch { /* an unrecognised shape reports nothing, which Absent's error already covers */ }
        return counts;
    }

    /// <summary>
    /// The reading as the lines the emitted script carries, each already a PowerShell comment. English and
    /// unlocalized for <see cref="ProfileLink"/>'s reason, and capped per list because a real
    /// <c>allow</c> can hold three hundred rules — the count is the decision and the first few are what
    /// make it concrete.
    /// </summary>
    public static IEnumerable<string> Lines(Reading reading, int show = 6)
    {
        if (reading.Error is { Length: > 0 } why) { yield return "#   " + why + "."; yield break; }
        if (reading.Empty)
        {
            yield return "#   The two are already the same, so this decision is not one you have to make.";
            yield break;
        }

        yield return $"#   {reading.Granting} entry(ies) would GRANT something that is not granted today"
                     + (reading.Narrowing > 0 ? $", and {reading.Narrowing} would take something away." : ".");
        foreach (Widening w in reading.Lists)
        {
            yield return $"#   {w.List}{(w.Narrows ? "  (arriving here NARROWS what an account can do)" : "")}";
            foreach ((string side, string[] added) in new[]
                     { ("-> keeps-its-files", w.ToPrimary), ("-> becomes-links", w.ToSecondary) })
            {
                if (added.Length == 0) continue;
                yield return $"#     {added.Length} {side}:";
                foreach (string rule in added.Take(show)) yield return "#       " + rule;
                if (added.Length > show) yield return $"#       ... and {added.Length - show} more";
            }
        }
        foreach (HookGap h in reading.Hooks)
            yield return $"#   hooks.{h.Event}: {h.OnPrimary} command(s) on the side that keeps its files, "
                         + $"{h.OnSecondary} on the other - a hook is a command line that runs";
    }
}
