using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeTray;

/// <summary>
/// Part of <see cref="SelfTestCli"/> — a document held against the thing it documents.
///
/// <para>One class in several files, on this repository's own rule for a type with many
/// independent surfaces (T133–T134, applied here by T381): the suite reached 9,330 lines and
/// 76 sections in one file, and adding a section meant scrolling past the whole of it to find
/// whether the helper you wanted already existed. <c>Run</c> keeps the ordered list of
/// sections — it is the table of contents, and its order is load-bearing — along with
/// <c>Check</c>, <c>Skip</c>, <c>Temp</c>, <c>CodeOf</c>, <c>Repo</c> and the counters, which
/// are the suite's own vocabulary.</para>
/// </summary>
internal static partial class SelfTestCli
{
    /// <summary>
    /// The one assertion here with reach: it turns "write the flag down in <c>dev-flags</c>" from a
    /// convention into something a build checks (T207). A catalogue in prose and a table in code are two
    /// sources of truth for one list, and the drift is always the same direction — the table gains a row
    /// and the document keeps describing the old set.
    ///
    /// <para>Both directions are asserted, because they catch opposite mistakes: every row must be named
    /// in the skill (a variant added and never documented), and every name the skill lists in a
    /// <c>| name</c> position must exist in the table (one renamed or removed, leaving the document
    /// promising a flag that now refuses).</para>
    /// </summary>
    private static void SkillCatalogue(IReadOnlyList<ToastPreviews.Variant> toasts,
                                       IReadOnlyList<TooltipCli.Variant> tips)
    {
        Repo("the dev-flags catalogue names what the tables declare",
             root => SkillCatalogue(root, toasts, tips), DevFlags);
    }

    /// <summary>The catalogue two checks read, spelled once.</summary>
    private const string DevFlags = ".claude/skills/dev-flags/SKILL.md";

    private static void SkillCatalogue(string root, IReadOnlyList<ToastPreviews.Variant> toasts,
                                       IReadOnlyList<TooltipCli.Variant> tips)
    {
        string[] lines = File.ReadAllLines(Path.Combine(root, DevFlags));
        string toastBlock = SkillBlock(lines, "--simulate-reset");
        string weekBlock = SkillBlock(lines, "--settings System --sample week=");

        if (!Check("the dev-flags catalogue still has the blocks these tables document",
                   toastBlock.Length > 0 && weekBlock.Length > 0,
                   $"--simulate-reset {(toastBlock.Length > 0 ? "found" : "MISSING")}, " +
                   $"week= {(weekBlock.Length > 0 ? "found" : "MISSING")} — the check cannot read what it compares"))
            return;

        string[] undocumented = toasts.Select(v => v.Name)
            .Where(n => !toastBlock.Contains(n, StringComparison.Ordinal)).ToArray();
        Check($"every toast the table declares is named in dev-flags ({toasts.Count})",
              undocumented.Length == 0,
              $"{string.Join(", ", undocumented)} — a card the catalogue does not mention");

        string[] promised = ListedNames(toastBlock)
            .Where(n => toasts.All(v => !v.Name.Equals(n, StringComparison.Ordinal))).ToArray();
        Check("and every toast dev-flags lists exists in the table", promised.Length == 0,
              $"{string.Join(", ", promised)} — documented, and the flag would refuse it");

        string[] weeks = Enum.GetNames<AccountFixture.SampleWeek>();
        string[] weeksMissing = weeks
            .Where(w => !weekBlock.Contains(w.ToLowerInvariant(), StringComparison.Ordinal)).ToArray();
        Check($"every week= name is named in dev-flags ({weeks.Length})", weeksMissing.Length == 0,
              string.Join(", ", weeksMissing));

        string[] weeksPromised = ListedNames(weekBlock)
            .Where(n => !weeks.Any(w => w.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray();
        Check("and every week= dev-flags lists exists", weeksPromised.Length == 0,
              string.Join(", ", weeksPromised));

        // The table this block itself added, held to the rule it just wrote (T214): a flag documented
        // the day it ships and never again is the drift this assertion exists to stop.
        string tipBlock = SkillBlock(lines, "--tooltip");
        if (!Check("the dev-flags catalogue has the --tooltip block", tipBlock.Length > 0,
                   "MISSING — the check cannot read what it compares"))
            return;

        string[] tipsUndocumented = tips.Select(v => v.Name)
            .Where(n => !tipBlock.Contains(n, StringComparison.Ordinal)).ToArray();
        Check($"every tooltip variant is named in dev-flags ({tips.Count})", tipsUndocumented.Length == 0,
              string.Join(", ", tipsUndocumented));

        string[] tipsPromised = ListedNames(tipBlock)
            .Where(n => tips.All(v => !v.Name.Equals(n, StringComparison.Ordinal))).ToArray();
        Check("and every tooltip dev-flags lists exists", tipsPromised.Length == 0,
              string.Join(", ", tipsPromised));
    }

    /// <summary>One flag's entry in the skill's fenced catalogue: the line that opens with it, plus the
    /// indented <c>#</c> continuations under it, which is how every entry in that file is written.</summary>
    private static string SkillBlock(string[] lines, string flag)
    {
        int start = Array.FindIndex(lines, l => l.StartsWith(flag, StringComparison.Ordinal));
        if (start < 0) return "";

        var block = new StringBuilder(lines[start]);
        for (int i = start + 1; i < lines.Length && lines[i].TrimStart().StartsWith('#'); i++)
            block.Append(' ').Append(lines[i].TrimStart());
        return block.ToString();
    }

    /// <summary>The names a catalogue entry lists as alternatives — the <c>| name</c> positions, which is
    /// the shape every such entry in that file uses.</summary>
    private static string[] ListedNames(string block) =>
        System.Text.RegularExpressions.Regex.Matches(block, @"\|\s*([a-z][a-z0-9-]*)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// <c>CHANGELOG.md</c> opens with a table mapping every block letter to its theme, and that table is
    /// what the next task is filed against — a letter missing from it reads exactly like a letter that
    /// does not exist, which is how this repository reached AH by opening a block per batch of findings
    /// instead of reusing the theme (T223).
    ///
    /// <para>The rule was already written down: the row is added by hand in the same commit as the
    /// block's first task, and the <c>roadmap-docs</c> skill calls it the one hand-edit to a governed
    /// file the discipline allows. What was missing is anything that notices when it is not done —
    /// <c>roadkeep lint</c> passes, because the table is prose to it. Two of the thirty-six headings had
    /// no row when this was written, one of them carrying five shipped tasks.</para>
    ///
    /// <para>Both directions are asserted, because they catch opposite mistakes: a heading with no row
    /// (the block shipped and the index never learned) and a row naming no heading (a letter renamed or
    /// retired, leaving the index pointing at nothing). The <b>anchor</b> is asserted too — it is
    /// derived from the heading exactly as GitHub derives it, so a heading reworded without its row is a
    /// link that lands on the top of the page rather than on the block.</para>
    ///
    /// <para>The <c>roadmap-docs</c> skill's own theme table is deliberately <em>not</em> a third source
    /// to check for completeness: it lists the themes that are meant to be reused, not every historical
    /// batch letter, so demanding a row per heading there would argue for exactly the sprawl it exists to
    /// stop. Only the one direction that cannot be right — a letter the skill names and the ledger does
    /// not declare — is asserted.</para>
    ///
    /// <para><b>The one part of a row that is a claim about another file (T244).</b>
    /// <c>(active — see ROADMAP)</c> says a block still has open work, and T223 checked everything about a
    /// row except that. It was wrong in both directions on six of the thirty-six: five blocks carried the
    /// marker with nothing open, and <c>G</c> had an open task and no marker. It is the only part of the
    /// index that answers <em>should I look here</em> — a wrong marker sends the next reader to a heading
    /// with nothing under it, or reads an active theme as a finished one, which is the habit that grew this
    /// ledger a letter per batch of findings.</para>
    ///
    /// <para>§XXII.6 left open whether to require the marker or only refuse a wrong one. It is
    /// <b>required</b>, both ways: unlike the entries under a heading, the marker is not frozen prose about
    /// what shipped — it is a live reading of <c>ROADMAP.md</c>, and every row that had drifted could be
    /// corrected in the commit that added this. Open is derived rather than spelled: the roadmap holds
    /// unshipped work only, so <em>any</em> task line under a heading is an open one, and the check needs
    /// no copy of the marker vocabulary to go stale beside <c>roadkeep.toml</c>'s.</para>
    /// </summary>
    private static void LedgerIndex() =>
        Repo("the ledger's index of blocks lists every block it holds", LedgerIndex,
             "CHANGELOG.md", "ROADMAP.md", RoadmapDocs);

    /// <summary>The skill whose theme table this check reads.</summary>
    private const string RoadmapDocs = ".claude/skills/roadmap-docs/SKILL.md";

    private static void LedgerIndex(string root)
    {
        string[] lines = File.ReadAllLines(Path.Combine(root, "CHANGELOG.md"));
        var headings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            Match m = Regex.Match(line, @"^## Block ([A-Z]+) [—-] .+$");
            if (m.Success) headings[m.Groups[1].Value] = Anchor(line[3..]);
        }

        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        var themes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            Match m = Regex.Match(line, @"^\| \[([A-Z]+)\]\(#([^)]+)\) \| (.+) \|$");
            if (!m.Success) continue;
            rows[m.Groups[1].Value] = m.Groups[2].Value;
            themes[m.Groups[1].Value] = m.Groups[3].Value;
        }

        // The precondition, first: an unreadable file parses to two empty lists, over which every claim
        // below holds vacuously — the failure mode this whole section exists to refuse.
        if (!Check("the ledger still has block headings and an index table",
                   headings.Count >= 30 && rows.Count >= 30,
                   $"{headings.Count} headings, {rows.Count} rows — the check cannot read what it compares"))
            return;

        string[] unlisted = headings.Keys.Where(b => !rows.ContainsKey(b)).OrderBy(b => b, StringComparer.Ordinal).ToArray();
        Check($"every block heading has a row in the ledger's index ({headings.Count})", unlisted.Length == 0,
              $"{string.Join(", ", unlisted)} — shipped under a letter the index does not list at all");

        string[] phantom = rows.Keys.Where(b => !headings.ContainsKey(b)).OrderBy(b => b, StringComparer.Ordinal).ToArray();
        Check("and every row in the index names a heading that exists", phantom.Length == 0,
              $"{string.Join(", ", phantom)} — indexed, and the link lands nowhere");

        string[] adrift = rows.Where(r => headings.TryGetValue(r.Key, out string? a) && a != r.Value)
                              .Select(r => $"{r.Key} (#{r.Value} → #{headings[r.Key]})")
                              .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Check("and every row's anchor still resolves to its heading", adrift.Length == 0,
              $"{string.Join(", ", adrift)} — the heading was reworded and the row was not");

        // The roadmap holds unshipped work only, so a task line under a heading IS an open one — no copy
        // of roadkeep.toml's marker vocabulary to drift out of step with it.
        var open = new HashSet<string>(StringComparer.Ordinal);
        string? block = null;
        foreach (string line in File.ReadAllLines(Path.Combine(root, "ROADMAP.md")))
        {
            Match head = Regex.Match(line, @"^## Block ([A-Z]+) ");
            if (head.Success) { block = head.Groups[1].Value; continue; }
            if (block is not null && Regex.IsMatch(line, @"^- .*\*\*T\d+\*\*")) open.Add(block);
        }

        string[] mismarked = themes
            .Where(t => open.Contains(t.Key) != t.Value.Contains("(active", StringComparison.Ordinal))
            .Select(t => open.Contains(t.Key) ? $"{t.Key} (open, unmarked)" : $"{t.Key} (marked, nothing open)")
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Check($"every row's active marker matches what the roadmap holds open ({open.Count} active)",
              mismarked.Length == 0,
              $"{string.Join(", ", mismarked)} — the index answers 'should I look here' and is wrong");

        string skill = Path.Combine(root, RoadmapDocs);

        // One direction only, and the doc comment says why the other would be wrong.
        string[] invented = Regex.Matches(File.ReadAllText(skill), @"^\|[^|]+\| \*\*([A-Z]+)\*\*", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(b => !headings.ContainsKey(b))
            .OrderBy(b => b, StringComparer.Ordinal).ToArray();
        Check("the theme table names only blocks the ledger declares", invented.Length == 0,
              $"{string.Join(", ", invented)} — a theme filed under a letter no block heading declares");
    }

    /// <summary>
    /// The per-file map against the tree it maps (T242). T219 moved it to the <c>file-map</c> skill on the
    /// argument that reference material is consulted rather than read — which is exactly when a hole in it
    /// costs something, because it is opened by a reader who does not already know the answer.
    ///
    /// <para>The map is a hand-written copy of <c>src\</c> and the tree is edited daily, so it drifts in
    /// three ways nothing would report: a new file with no row, a row naming a file that was renamed or
    /// deleted, and a row left under the folder a file has since moved out of. One of each was already
    /// there when this was written — eight undocumented files, and <c>ThroughputFixture</c> filed under the
    /// Context heading for a file that lives in <c>src\Usage\</c>.</para>
    ///
    /// <para><b>What counts as documented.</b> A row names one type, and this repository deliberately
    /// spreads one class over several files — <c>StatisticsPage.{Throughput,Chart,…}.cs</c> is one class in
    /// six files (T133–T134), and a XAML page is a pair. So a file is covered by its own row <em>or</em> by
    /// the row for its stem, everything before the first dot; a page's row is written
    /// <c>Foo.xaml(.cs)</c> and both halves of the pair must exist. Requiring a row per file would make the
    /// map longer than the tree and would argue against the partial-file convention.</para>
    ///
    /// <para>The folder table left in <c>AGENTS.md</c> is checked the same way and for a sharper reason: a
    /// new subsystem folder is precisely the moment placement matters, and that table — not the skill — is
    /// what a reader hits first, being in the file loaded every turn.</para>
    /// </summary>
    private static void FileMap() =>
        Repo("the file map names every source file, and only files that exist", FileMap,
             ".claude/skills/file-map/SKILL.md", "AGENTS.md", "src");

    private static void FileMap(string root)
    {
        string skill = Path.Combine(root, ".claude/skills/file-map/SKILL.md");
        string agents = Path.Combine(root, "AGENTS.md");
        string src = Path.Combine(root, "src");
        string[] files = Directory.Exists(src)
            ? Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
                       .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToArray()
            : Array.Empty<string>();

        string[] rows = Regex.Matches(File.ReadAllText(skill), @"^\| `(src/[^`]+)`", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // The precondition, first: an unreadable file or a renamed folder parses to two empty lists, over
        // which both claims below hold vacuously.
        if (!Check("the file map and the source tree are both readable",
                   files.Length >= 40 && rows.Length >= 40,
                   $"{files.Length} .cs files, {rows.Length} rows — the check cannot read what it compares"))
            return;

        // `Foo.xaml(.cs)` is the pair; every other row is one path. Both halves have to be on disk, or the
        // shorthand is hiding half a page that no longer exists.
        string[] absent = rows
            .SelectMany(r => r.EndsWith(".xaml(.cs)", StringComparison.Ordinal)
                ? new[] { r[..^"(.cs)".Length], r[..^"(.cs)".Length] + ".cs" }
                : new[] { r })
            .Where(p => !File.Exists(Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar))))
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Check($"every path the file map names exists ({rows.Length} rows)", absent.Length == 0,
              $"{string.Join(", ", absent)} — documented, and renamed or deleted since");

        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string row in rows)
        {
            string path = row.Replace(".xaml(.cs)", ".xaml.cs");
            covered.Add(path);
            covered.Add(Stem(path));   // so a page's row covers the `partial` files spread beside it
        }
        string[] undocumented = files.Where(f => !covered.Contains(f) && !covered.Contains(Stem(f)))
                                     .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Check($"every source file is named by a row or by its stem's ({files.Length} files)",
              undocumented.Length == 0,
              $"{string.Join(", ", undocumented)} — in the tree and in no row of the map");

        string[] folders = Directory.Exists(src)
            ? Directory.GetDirectories(src).Select(d => "src/" + Path.GetFileName(d) + "/")
                       .OrderBy(d => d, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        string[] listed = Regex.Matches(File.ReadAllText(agents), @"^\| `(src/[A-Za-z]+/)` \|",
                                        RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();

        string[] unplaced = folders.Where(f => !listed.Contains(f, StringComparer.Ordinal)).ToArray();
        Check($"AGENTS.md's folder table names every subsystem folder ({folders.Length})", unplaced.Length == 0,
              $"{string.Join(", ", unplaced)} — a folder with no stated place for a new file in it");

        string[] gone = listed.Where(f => !folders.Contains(f, StringComparer.Ordinal))
                              .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Check("and every folder it names is still there", gone.Length == 0,
              $"{string.Join(", ", gone)} — a placement rule for a folder that no longer exists");
    }

    /// <summary>
    /// T259. Every <c>.ps1</c> this repository carries is one PowerShell 5.1 will read the way it was
    /// written — because the version that ships in Windows reads a script with no byte-order mark in the
    /// <b>ANSI code page</b>, not as UTF-8.
    ///
    /// <para><b>What that does, measured on two reduced files.</b> The three UTF-8 bytes of an em dash
    /// arrive as three CP1252 characters whose last one is a right double quotation mark. Inside a
    /// double-quoted string that closes the string mid-expression and the whole script fails to parse, so
    /// <em>no</em> assertion runs; inside a comment it is merely mojibake. Both were hit in one session:
    /// the loud one adding a message to the interaction check, and the quiet one in T234, where a
    /// <c>-split</c> on a middle dot compared against a character the parser had read as something else,
    /// never matched, and asserted nothing while passing.</para>
    ///
    /// <para><b>The predicate is the property, not the fix.</b> A file that is pure ASCII has nothing to
    /// misread and needs no mark; a file carrying any character above 127 needs one. So the rule is "ASCII,
    /// or marked", which accepts both answers the task weighed and rejects the state that produced the
    /// defect — five of the six scripts, every one of them carrying prose and none of them marked.</para>
    /// </summary>
    private static void ScriptEncoding() =>
        Repo("every .ps1 is one PowerShell 5.1 reads as written", ScriptEncoding, "scripts", "build");

    /// <summary>
    /// T347. No source file carries a NUL byte, because git calls such a file <em>binary</em>.
    ///
    /// <para>The byte that prompted this was deliberate and correct as a value:
    /// <c>SessionIndex</c> joined a cache key on a character no path can contain, which is exactly
    /// what an unambiguous separator should be. What was wrong was writing it as a <em>literal</em>
    /// byte in the source instead of the escape that produces one. The cost is entirely outside the
    /// running program: the file had no line diff, ever — a nine-line change to it was reported as
    /// <c>1141 +++---</c>, the sum of both versions — and <c>blame</c>, <c>add -p</c> and this
    /// repository's own grep all degraded the same way, the last of them answering "binary file
    /// matches" and nothing else.</para>
    ///
    /// <para>Byte-wise and deliberately not as text, for the same reason the <c>.ps1</c> check reads
    /// bytes: the question is what a <em>decoder</em> does with the file, and reading it as text
    /// first answers with whichever decoder this check happened to use.</para>
    /// </summary>
    private static void SourceIsText() =>
        Repo("no source file is binary to git", SourceIsText, "src");

    private static void SourceIsText(string root)
    {
        string[] sources = Directory.GetFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();

        if (!Check($"the sources are there to read ({sources.Length})", sources.Length > 0,
                   "no source file found, so this would pass over nothing"))
            return;

        var binary = new List<string>();
        foreach (string path in sources)
        {
            int nuls = File.ReadAllBytes(path).Count(b => b == 0);
            if (nuls > 0) binary.Add($"{Path.GetFileName(path)} ({nuls})");
        }

        Check($"no source file carries a NUL byte ({sources.Length} files)", binary.Count == 0,
              $"{string.Join(", ", binary)} — git calls a file with one binary, so it has no line " +
              "diff, no blame and no grep; write the escape rather than the byte");
    }

    /// <summary>
    /// T362. No source file holds text that is UTF-8 <em>read as CP1252 and written back</em> — the
    /// state the About page's four link cards were in, drawing four Latin-1 characters apiece where an
    /// octopus, a package, a bug and a star belong. (This comment cannot show the broken form: it would
    /// be an occurrence, and this check would report itself.)
    ///
    /// <para><b>Why the two encoding checks already here did not cover it.</b> T259 asks whether a
    /// <c>.ps1</c> has a mark, and T347 whether a source has a NUL: both are about a <em>decoder</em>
    /// failing loudly. Double-encoded text decodes perfectly. It is well-formed UTF-8 that happens to
    /// spell the wrong characters, so nothing downstream complains and only a person looking at the
    /// window can tell — which is how five visible literals and thirty-two comments survived since
    /// T310.</para>
    ///
    /// <para><b>The predicate is a run, not a line.</b> Mojibake is a lead character mapping to one of
    /// the four lead bytes below, followed by the right number of characters mapping into
    /// <c>80..BF</c>. Asking the question of a whole line instead — does this line round-trip? — would
    /// miss any line that also carries one correctly-encoded dash, because that dash maps to a byte no
    /// valid sequence can start. Scanning runs makes each occurrence independent of its neighbours.</para>
    ///
    /// <para><b>T363: four lead bytes, not all of <c>C2..F4</c>.</b> The wider range accepts
    /// <c>0.31×–1.81×</c> — a multiplication sign is <c>D7</c> and an en dash is <c>96</c>, so the pair
    /// decodes, to the Hebrew letter zayin. That is correct text this check would call damage, and it
    /// stayed quiet only because the one line in this repository carrying it (<c>CHANGELOG.md:244</c>)
    /// was out of scope. Narrowing costs no detection: replayed against both files as they stood at
    /// <c>d83ddd3</c>, before T362's repair, the four leads still report all 37 damaged lines, and over
    /// every text file in the tree they report nothing.</para>
    ///
    /// <para><b>The localization files are in scope, and T362's reasoning for leaving them out was
    /// measured wrong.</b> It argued the false positive would live in <c>lang\*.json</c> because that is
    /// where the accents are. Zero hits there, across 3333 non-ASCII lines; the one false positive was
    /// in Markdown prose, which the exclusion never covered. Those five files are where a round trip
    /// would corrupt every user-visible string in five languages at once, and the key check beside this
    /// one reads whether a key <em>resolves</em>, never what its value spells.</para>
    /// </summary>
    private static void SourceIsNotDoubleEncoded() =>
        Repo("no source or lang file is UTF-8 that was read as CP1252", SourceIsNotDoubleEncoded,
             "src", "lang");

    /// <summary>CP1252 for <c>0x80..0x9F</c>, the one range where it is not Latin-1 - written as
    /// escapes rather than as the characters themselves, for two reasons. Five of the thirty-two are
    /// invisible C1 controls: Windows leaves those bytes undefined and passes them through, which is
    /// what put a U+0090 in the middle of the octopus, and an invisible character in a table is one no
    /// reviewer can check. And a table spelled literally is a table this very check could not survive
    /// being applied to.</summary>
    private const string Cp1252High =
        "\u20ac\u0081\u201a\u0192\u201e\u2026\u2020\u2021" +  // 80..87
        "\u02c6\u2030\u0160\u2039\u0152\u008d\u017d\u008f" +  // 88..8F
        "\u0090\u2018\u2019\u201c\u201d\u2022\u2013\u2014" +  // 90..97
        "\u02dc\u2122\u0161\u203a\u0153\u009d\u017e\u0178";   // 98..9F

    /// <summary>The byte this character would have been in CP1252, or -1 if it was never one.</summary>
    private static int Cp1252Byte(char c)
    {
        if (c < 0x80 || (c >= 0xA0 && c <= 0xFF)) return c;
        int high = Cp1252High.IndexOf(c);
        return high < 0 ? -1 : 0x80 + high;
    }

    /// <summary>The four UTF-8 lead bytes the characters this project writes actually use: <c>C2</c> for
    /// <c>§</c> and <c>·</c>, <c>C3</c> for accented Latin, <c>E2</c> for punctuation, arrows, <c>⚠</c>
    /// and <c>⭐</c>, and <c>F0</c> for emoji. Every other lead in <c>C2..F4</c> is a byte this repository
    /// only ever produces by accident — and one of them, <c>D7</c>, is the multiplication sign, which
    /// makes correct arithmetic prose look like damage (T363).</summary>
    private static readonly int[] MojibakeLeads = { 0xC2, 0xC3, 0xE2, 0xF0 };

    /// <summary>The character this run spells when read back as UTF-8, or -1 if it spells none.</summary>
    private static int DoubleEncodedAt(string s, int at)
    {
        int lead = Cp1252Byte(s[at]);
        if (Array.IndexOf(MojibakeLeads, lead) < 0) return -1;

        int follow = lead >= 0xF0 ? 3 : lead >= 0xE0 ? 2 : 1;
        if (at + follow >= s.Length) return -1;

        int code = lead & (0x7F >> (follow + 1));
        for (int i = 1; i <= follow; i++)
        {
            int b = Cp1252Byte(s[at + i]);
            if (b < 0x80 || b > 0xBF) return -1;
            code = (code << 6) | (b & 0x3F);
        }

        // Overlong and out-of-range sequences are not what an encoder produced, so they are not this.
        int floor = follow == 1 ? 0x80 : follow == 2 ? 0x800 : 0x10000;
        return code < floor || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF) ? -1 : code;
    }

    private static void SourceIsNotDoubleEncoded(string root)
    {
        string[] sources = Directory.GetFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Concat(Directory.GetFiles(Path.Combine(root, "lang"), "*.json"))
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();

        // The lang files are the point of T363's widening, so "some files were read" is not enough:
        // a scan that silently found none of them would pass while asserting nothing about them.
        int langFiles = sources.Count(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        if (!Check($"the sources are there to read ({sources.Length}, of which {langFiles} localization)",
                   sources.Length > 0 && langFiles == L.Codes.Count,
                   $"expected one .json per shipped language ({L.Codes.Count}), read {langFiles} — " +
                   "this would pass over the files it was widened to cover"))
            return;

        var damaged = new List<string>();
        foreach (string path in sources)
        {
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length && damaged.Count < 8; i++)
            {
                for (int at = 0; at < lines[i].Length; at++)
                {
                    int code = DoubleEncodedAt(lines[i], at);
                    if (code < 0) continue;
                    damaged.Add($"{Path.GetFileName(path)}:{i + 1} reads as {char.ConvertFromUtf32(code)}");
                    break;
                }
            }
        }

        Check($"no source or lang file holds double-encoded text ({sources.Length} files)",
              damaged.Count == 0,
              $"{string.Join("; ", damaged)} — this is UTF-8 an editor read as CP1252 and wrote back, " +
              "so it decodes cleanly and spells the wrong character; save the file as UTF-8");
    }

    private static void ScriptEncoding(string root)
    {
        string[] scripts = new[] { "scripts", "build" }
            .Select(d => Path.Combine(root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.ps1", SearchOption.AllDirectories))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!Check("the scripts are readable", scripts.Length >= 4,
                   $"found {scripts.Length} .ps1 — the check cannot read what it compares"))
            return;

        var unreadable = new List<string>();
        foreach (string path in scripts)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bool marked = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            if (marked) continue;

            // Byte-wise, deliberately: this is a question about what a *decoder* will do with the file,
            // and reading it as text first would answer with the decoder this check happens to use.
            int high = bytes.Count(b => b > 0x7F);
            if (high > 0)
                unreadable.Add($"{Path.GetFileName(path)} ({high} byte(s) over 127, and no mark)");
        }

        Check($"every .ps1 is ASCII or carries a UTF-8 mark ({scripts.Length})", unreadable.Count == 0,
              $"{string.Join(", ", unreadable)} — 5.1 reads these in the ANSI code page, so a dash inside a "
              + "string closes it and the script does not parse");
    }

    /// <summary>
    /// T285. <see cref="CodeOf"/>'s own answer, over the literal forms this repository contains — asserted
    /// against synthetic source rather than against the tree, for T248's reason: the tree happens not to
    /// hold every case today, and a check that waits for one is a check that ships broken.
    ///
    /// <para>Two directions, and the second is the one that fails quietly. A comment that survives makes a
    /// scan report something that is not there, which is loud — all three scans here were caught that way,
    /// each on its own first run. A string that does <em>not</em> survive makes a scan report less than is
    /// there, and a check asserting less still passes. Every fixture below therefore keeps a piece of code
    /// after the region being skipped, so a terminator read wrong is a fact this can see.</para>
    /// </summary>
    private static void CodeReading()
    {
        // Assembled, never written out — the fixtures are read by the very scans they are fixtures of.
        string q = "\"";
        string slashes = "//";
        string setter = "Console." + "OutputEncoding" + " =";

        (string What, string Source, bool Kept)[] cases =
        {
            ("a line comment", $"{slashes} {setter} UTF8\nint a = 1;", false),
            ("a doc comment", $"/// <c>{setter} UTF8</c>\nint a = 1;", false),
            ("a block comment", $"/* {setter} UTF8 */\nint a = 1;", false),
            ("a comment at end of line", $"int a = 1;   {slashes} {setter} UTF8", false),
            ("code itself", $"{setter} UTF8;", true),
            ("a string holding the setter", $"string s = {q}{setter} UTF8{q};", true),
        };
        foreach ((string what, string source, bool kept) in cases)
            Check($"{what}: the setter {(kept ? "survives" : "is gone")}",
                  CodeOf(source).Contains(setter) == kept, CodeOf(source).Replace("\n", "\\n"));

        // The T248 case, one level down: a `//` inside a literal is not a comment, and reading it as one
        // eats the rest of the line — silently, because what is left still parses and still asserts.
        string url = $"string site = {q}https://example.invalid/x{q}; int kept = 1;";
        Check("a // inside a string does not start a comment", CodeOf(url).Contains("int kept = 1;"),
              CodeOf(url));

        // Every terminator this file's own sources rely on. Each fixture ends in a token that only appears
        // after the literal, so a string read as unterminated loses it.
        (string What, string Source)[] terminators =
        {
            ("a doubled quote in a verbatim string", $"var r = @{q}a{q}{q}b{q}; int kept = 1;"),
            ("an escaped quote in a quoted string", $"var r = {q}a\\{q}b{q}; int kept = 1;"),
            ("a quote inside a char literal", $"char c = '{q}'; int kept = 1;"),
            ("a raw string carrying a quote", $"var r = {q}{q}{q}he said {q}hi{q}{q}{q}{q}; int kept = 1;"),
            ("a raw string carrying a comment", $"var r = {q}{q}{q}{slashes} not a comment{q}{q}{q}; int kept = 1;"),
            ("an interpolated raw string", $"var r = $${q}{q}{q}x {{{{y}}}} {slashes} z{q}{q}{q}; int kept = 1;"),
        };
        string[] lost = terminators.Where(t => !CodeOf(t.Source).Contains("int kept = 1;"))
                                   .Select(t => $"{t.What} → {CodeOf(t.Source)}").ToArray();
        Check($"the code after every literal form is still read ({terminators.Length})", lost.Length == 0,
              string.Join("; ", lost));

        // Line numbers survive a removal, or a caller counting by line answers about the wrong one.
        string across = $"/* one\ntwo */\nint a = 1;";
        Check("a removed region keeps its newlines",
              CodeOf(across).Count(ch => ch == '\n') == across.Count(ch => ch == '\n'),
              CodeOf(across).Replace("\n", "\\n"));

        // And every file of this suite, which between them are the largest sources here and the ones
        // carrying raw strings: a scanner that desynchronised on those would silently skip everything
        // after them. The claim is that the scan reached the end — a file whose last code line is the
        // class's closing brace is one nothing swallowed. Over all six rather than the one that used to
        // be the biggest (T381), because which of them carries the raw strings is not a fact to pin.
        string[] suite = (RepoFile(Path.Combine("src", "Cli", "SelfTestCli.cs")) is { } any
                ? Directory.GetFiles(Path.GetDirectoryName(any)!, "SelfTestCli*.cs")
                : Array.Empty<string>())
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        string[] swallowed = suite.Where(f => LastCode(CodeOf(File.ReadAllText(f))) != "}")
                                  .Select(Path.GetFileName).ToArray()!;
        Check($"the scan reaches the end of every file of this suite ({suite.Length})",
              suite.Length >= 2 && swallowed.Length == 0,
              suite.Length < 2 ? $"only {suite.Length} file(s) found, so nothing was compared"
                               : string.Join(", ", swallowed));
    }

    /// <summary>
    /// T284. <see cref="ApiClient.NamesRead"/> is the parser enumerating itself: the same method that
    /// parses a response is run against a lookup that records the name it was asked for and answers
    /// <c>null</c>. That is what makes the <c>--probe</c> read-out impossible to forget to update — and it
    /// is exact only while the parse has no branch in it.
    ///
    /// <para><b>The failure it cannot see.</b> A read written as <c>get("…-in-use") is "true" ?
    /// get("…-overage-reset") : null</c> is ordinary code. Under the recording lookup the condition is
    /// false, the second name is never asked for, and it is reported UNREAD — by the instrument built to
    /// say which names reach a field, with every check of the classifier green. Silent, and pointing the
    /// wrong way: a header the app reads, reported as one nothing does.</para>
    ///
    /// <para><b>So the property asserted is totality, from the source.</b> Every header name spelled in
    /// <c>ApiClient.cs</c> is a name the enumeration reports, and every name it reports is spelled there —
    /// which holds without asking anything about branches, and fails the moment a conditional read is
    /// written rather than the month somebody trusts the mark. The bare family prefix is excluded: it is
    /// what the verbatim copy filters on, not a name read into a field.</para>
    /// </summary>
    private static void ParserNames() =>
        Repo("every header name the parser spells is one it reports reading", ParserNames, "src");

    /// <summary>A header name as the parse spells one: the family, and at least one character of window
    /// after it. The bare <c>anthropic-ratelimit-</c> prefix is the verbatim copy's filter, not a name.</summary>
    private static readonly Regex HeaderLiteral = new("\"(anthropic-ratelimit-[a-z0-9][a-z0-9-]*)\"",
                                                      RegexOptions.Compiled);

    private static void ParserNames(string root)
    {
        string path = Path.Combine(root, "src", "Usage", "ApiClient.cs");
        if (!Check("the parser is in the checkout", File.Exists(path), path)) return;

        // Over the code alone (T285). This scan carried two hand rules — skip a line starting with `//`,
        // and refuse a name ending in a dash — the second only because the paragraphs above the parse write
        // the family as `anthropic-ratelimit-unified-*`. Neither is needed once a comment is a region
        // rather than a shape, and the bare family prefix is excluded by requiring a character after it:
        // that literal is what the verbatim copy filters on, not a name read into a field.
        var spelled = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in HeaderLiteral.Matches(CodeOf(File.ReadAllText(path))))
            spelled.Add(m.Groups[1].Value);

        if (!Check($"the parser spells header names at all ({spelled.Count})", spelled.Count > 0,
                   "none found — the scan reads nothing, so it would pass on a parser that read nothing"))
            return;

        string[] unreported = spelled.Where(n => !ApiClient.NamesRead.Contains(n, StringComparer.OrdinalIgnoreCase))
                                     .ToArray();
        Check("every name the source spells is one the enumeration reports", unreported.Length == 0,
              $"{string.Join(", ", unreported)} — read behind a branch the recording lookup does not take, "
              + "so --probe would mark it UNREAD (T284)");

        string[] unspelled = ApiClient.NamesRead.Where(n => !spelled.Contains(n)).ToArray();
        Check("and every name it reports is spelled in the source, not assembled", unspelled.Length == 0,
              $"{string.Join(", ", unspelled)} — a name the scan cannot see is one it cannot hold");

        Check($"so the two are the same set ({ApiClient.NamesRead.Count})",
              spelled.Count == ApiClient.NamesRead.Count, $"{spelled.Count} spelled, {ApiClient.NamesRead.Count} reported");
    }

    /// <summary>
    /// T283. The console's code page is set in exactly one place — where the flags are dispatched — and
    /// this is the check that keeps it there.
    ///
    /// <para>Twelve read-outs each opened with their own <c>Console.OutputEncoding = UTF8</c>, learned one
    /// at a time as each hit the same wall: a WinExe's console starts on the OEM code page, which prints an
    /// em dash and a section sign as <c>?</c>. <c>--probe</c> never learned it, and it was the read-out
    /// densest in both — every <c>§XVIII.9</c> it sent a reader to arrived as <c>?XVIII.9</c>, which names
    /// no section.</para>
    ///
    /// <para><b>Why a source scan rather than an output assertion.</b> The defect is not what one flag
    /// prints; it is that remembering was the mechanism. A check of <c>--probe</c>'s output would have gone
    /// green the moment somebody pasted the line a thirteenth time, and said nothing about the fourteenth
    /// flag. What is asserted is the shape that made the omission impossible: one call site, in the
    /// dispatch, ahead of every verb.</para>
    /// </summary>
    private static void ConsoleCodePage() =>
        Repo("the console's code page is set once, where the flags are dispatched", ConsoleCodePage, "src");

    private static void ConsoleCodePage(string root)
    {
        // Assembled, never written out: a literal of the thing being counted would be one more of it, in
        // the file doing the counting — the same rule FlagScanning's fixtures are built by.
        string setter = "Console." + "OutputEncoding" + " =";
        string dispatch = Path.Combine("src", "Tray", "Program.cs");

        // Counted over the code alone (T285). The paragraph above names the setter in order to explain what
        // is being counted, and on this check's first run that explanation was read as a thirteenth copy —
        // so the comment is removed lexically rather than by a rule about how a line begins.
        (string File, int Count)[] setters = Directory
            .GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => (File: Path.GetRelativePath(root, f),
                          Count: CodeOf(File.ReadAllText(f)).Split(setter).Length - 1))
            .Where(x => x.Count > 0)
            .ToArray();

        if (!Check("something sets the console's code page at all", setters.Length > 0,
                   "no source sets it — a read-out full of dashes would print them as '?'"))
            return;

        string[] elsewhere = setters
            .Where(x => !string.Equals(x.File, dispatch, StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.File} ({x.Count})").ToArray();
        Check("only the dispatch sets it, so no flag can be added having forgotten to",
              elsewhere.Length == 0,
              $"{string.Join(", ", elsewhere)} — a per-file copy is a rule twelve callers kept and a "
              + "thirteenth did not (T283)");
        Check("and it sets it once, not once per verb",
              setters.Single(x => string.Equals(x.File, dispatch, StringComparison.OrdinalIgnoreCase))
                     .Count == 1);
    }

    /// <summary>
    /// T272. Every build <c>check.yml</c> runs fails on a warning, because the alternative was tolerating
    /// them and that is measurably not a stable state.
    ///
    /// <para><b>What it is asserting and why it is here rather than only in the workflow.</b> Three
    /// <c>CS8602</c> warnings stood in this project long enough that a build log reading "3 warnings" was
    /// what clean looked like, so a fourth would have arrived unread. The gate that stops that lives in a
    /// YAML file nothing reads, which is the same shape as the defect: dropping <c>-warnaserror</c> from a
    /// step is one word, it makes no test fail, and the run stays green while the protection is gone. So
    /// the flag is asserted where the assertions are.</para>
    ///
    /// <para><b>Both steps, not one.</b> <c>check.yml</c> builds twice — once for the self-check job and
    /// once for the interaction job — and a gate carried by one of them only holds when that job is the one
    /// that ran. The check counts the build steps it found and prints the count, because "all of them
    /// carry it" is not an assertion when the number could be zero.</para>
    /// </summary>
    private static void WarningGate() =>
        Repo("every build CI runs fails on a warning", WarningGate, ".github/workflows/check.yml");

    private static void WarningGate(string root)
    {
        string[] builds = File.ReadAllLines(Path.Combine(root, ".github/workflows/check.yml"))
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("run:", StringComparison.Ordinal)
                        && l.Contains("dotnet build", StringComparison.Ordinal))
            .ToArray();

        // The count is the precondition, not a nicety: a workflow this check cannot find the builds in
        // would pass an "all of them" test with nothing in hand.
        if (!Check("the workflow's builds are readable", builds.Length == 2,
                   $"found {builds.Length} `dotnet build` step(s), expected 2 — the check cannot gate what " +
                   "it cannot see"))
            return;

        string[] ungated = builds.Where(l => !l.Contains("-warnaserror", StringComparison.Ordinal)).ToArray();
        Check($"both CI builds carry -warnaserror ({builds.Length})", ungated.Length == 0,
              $"{string.Join(" | ", ungated)} — without it a real warning lands in a log nobody reads, " +
              "which is the state T272 was filed against");
    }

    /// <summary>
    /// T349. The .NET SDK patch is named in <c>global.json</c> and nowhere else, so what a tag publishes is a
    /// function of the tag rather than of the calendar.
    ///
    /// <para><b>What went wrong without it.</b> One source produced a 75,792,286-byte <c>.exe</c> under SDK
    /// 10.0.302 and 75,881,079 under 10.0.303 — 89 KB from nothing anybody wrote, because a self-contained
    /// single file bundles whatever runtime the SDK ships. Nothing named a version: there was no
    /// <c>global.json</c>, and every <c>setup-dotnet</c> step asked for <c>10.0.x</c>, which resolves to
    /// whatever the runner happened to have that week. <c>update-winget.ps1</c> pins an
    /// <c>InstallerSha256</c> from the installer that was actually built, so a release rebuilt from its tag
    /// leaves the published manifest stale against the binary.</para>
    ///
    /// <para><b>Why this is a check and not a paragraph.</b> The pin is one line of YAML per step, and the
    /// way it comes undone is not an edit to those lines: it is a <em>fourth</em> step, added by somebody
    /// wiring up a new job, carrying the <c>dotnet-version: '10.0.x'</c> that every example on the internet
    /// carries. Nothing fails. The new job builds, the run is green, and the guarantee is gone for whichever
    /// job that is. So the assertion is over every <c>setup-dotnet</c> step there is, and the count is
    /// printed, because "all of them" is not a claim when the number could be zero.</para>
    ///
    /// <para><b><c>rollForward</c> is asserted present, not asserted equal.</b> Which policy is right is a
    /// judgement about how much of a contributor's machine this project is entitled to govern, and it may
    /// change; a <c>global.json</c> that states no policy at all is a different thing — the SDK's default is
    /// <c>latestPatch</c>, which floats within the band and is the hole this task closed.</para>
    /// </summary>
    private static void SdkPin() =>
        Repo("the SDK patch is pinned, and every build resolves through the pin", SdkPin,
             "global.json", ".github/workflows/build.yml", ".github/workflows/check.yml");

    private static void SdkPin(string root)
    {
        System.Text.Json.JsonElement sdk;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(root, "global.json")));
            if (!doc.RootElement.TryGetProperty("sdk", out sdk))
            {
                Check("global.json names an sdk", false, "no `sdk` object — it pins nothing");
                return;
            }
            sdk = sdk.Clone();
        }
        catch (System.Text.Json.JsonException e)
        {
            Check("global.json is readable JSON", false, e.Message);
            return;
        }

        string version = sdk.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
        Check($"global.json pins an exact SDK patch ({(version.Length > 0 ? version : "none")})",
              Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"),
              $"`{version}` is not a three-part version — a band is what T349 was filed against");
        Check("and states a roll-forward policy rather than taking the default",
              sdk.TryGetProperty("rollForward", out var rf) && (rf.GetString() ?? "").Length > 0,
              "no `rollForward` — the SDK default is latestPatch, which floats inside the band the " +
              "version just named");

        // Every setup-dotnet step in every workflow, by the `uses:` line and the `with:` block under it.
        var literal = new List<string>();
        int steps = 0;
        foreach (string file in Directory.GetFiles(Path.Combine(root, ".github", "workflows"), "*.yml"))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("uses: actions/setup-dotnet", StringComparison.Ordinal)) continue;
                steps++;

                // The rest of this step: the keys under it are indented at least as far as `uses:` itself,
                // and the next step is the first line that dedents past it or opens a new `- ` item.
                int indent = lines[i].Length - lines[i].TrimStart().Length;
                bool pinned = false;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    string body = lines[j].TrimStart();
                    if (body.Length == 0) continue;
                    if (lines[j].Length - body.Length < indent || body.StartsWith("- ", StringComparison.Ordinal))
                        break;
                    if (body.StartsWith("global-json-file:", StringComparison.Ordinal)) pinned = true;
                }
                if (!pinned) literal.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }

        if (!Check("the workflows' setup-dotnet steps are readable", steps > 0,
                   "found none — the check cannot pin what it cannot see"))
            return;

        Check($"every setup-dotnet step resolves through global.json ({steps})", literal.Count == 0,
              $"{string.Join(", ", literal)} — a step naming its own version is a second source of truth " +
              "for the runtime that ships, and the one that drifts is whichever job nobody watched");
    }

    /// <summary>
    /// T248. The flag scan's own two failure modes, held up by the two lines that produced them.
    ///
    /// <para>Its first run ever (T243) failed on <c>--x</c>: the paragraph above the method quotes the
    /// shape being matched, and the scan read the example as a switch the catalogue had forgotten. The fix
    /// — strip <c>//</c> to end of line — is right about comments and approximate about strings, and the
    /// approximation is the second failure: a <c>//</c> inside a literal ends the line for the scan, so a
    /// real flag sharing that line stops being seen. That one is invisible, because a check that asserts
    /// less still passes.</para>
    ///
    /// <para>Both are asserted against synthetic source rather than against the tree, which is the point:
    /// the tree happens not to contain the second case today, and a check that waits for one is a check
    /// that ships broken. The prose sample is this very file's own shape, quoted flag and all.</para>
    /// </summary>
    private static void FlagScanning()
    {
        // The samples are ASSEMBLED, never written out. A snippet containing a literal `"--sample"` is,
        // to the scan that reads this very folder, a flag the .exe accepts and the catalogue forgot — and
        // it would be right. Building the token means the fixture cannot be mistaken for the thing it is a
        // fixture of, which is the same reason the check below has to exist at all.
        static string Lit(string name) => '"' + "--" + name + '"';

        string prose = $$"""
            /// <para>The source side is every {{Lit("gone-fishing")}} literal under src\, which is
            /// what makes this derivable — one shape covers args.Contains and args[0] == alike.</para>
            // Comments first: the paragraph above quotes {{Lit("also-gone")}} to show what is matched.
            """;
        string[] fromProse = FlagsRead(prose).ToArray();
        Check("a paragraph quoting a flag is not a flag the app accepts",
              fromProse.Length == 0, string.Join(", ", fromProse));

        // The line the stripper ate. Everything after the `//` inside the literal used to vanish, and with
        // it a real switch — silently, because what is left still parses and still passes.
        string afterUrl = $"""
            string site = "https://example.invalid/x"; bool raw = flags.Contains({Lit("kept-anyway")});
            """;
        Check("a flag after a string containing // is still read",
              FlagsRead(afterUrl).SequenceEqual(new[] { "--kept-anyway" }),
              string.Join(", ", FlagsRead(afterUrl)));

        // Every comparison shape the sources actually use, so narrowing did not quietly drop one. A shape
        // missing from here would show up as a *declared* flag nothing reads — see FlagCatalogue.
        (string What, string Code)[] shapes =
        {
            ("Contains",        $"if (args.Contains({Lit("one")})) {{ }}"),
            ("== on the left",  $"if (args[0] == {Lit("two")}) {{ }}"),
            ("== on the right", $"if ({Lit("three")} == args[0]) {{ }}"),
            ("an is pattern",   $"bool x = a is {Lit("four")};"),
            ("IndexOf",         $"int at = Array.IndexOf(args, {Lit("five")});"),
        };
        string[] missed = shapes.Where(s => !FlagsRead(s.Code).Any()).Select(s => s.What).ToArray();
        Check($"every comparison shape the sources use is read ({shapes.Length})", missed.Length == 0,
              $"{string.Join(", ", missed)} — a flag read this way would read as documented-but-unused");
    }

    /// <summary>
    /// The other half of the flag surface (T243): the switches that are a bare string read out of
    /// <c>args</c> where they are used, with no table in code for T207's assertion to compare against.
    ///
    /// <para>T207 reaches exactly the flags whose variants <em>are</em> a list a build can enumerate — the
    /// toast cards, the tooltip variants, the <c>week=</c> names. Everything else — <c>--probe</c>'s three
    /// switches, <c>--activity</c>'s four, <c>--raw</c>, <c>--sample-env</c> — was documented because
    /// somebody remembered, which is the guarantee that assertion exists to replace. <c>--recorded</c>
    /// reached the catalogue that way one task ago; <c>--raw</c> did not reach it at all, and had been
    /// unlisted since <c>--live</c> shipped.</para>
    ///
    /// <para><b>Where the catalogue is read matters, and this is what §XXII.5 left to settle.</b> Taken
    /// over the whole document the second direction cannot be trusted: the prose explains other programs'
    /// flags, and <c>claude auth status --json</c> reads as this app promising a <c>--json</c> it has never
    /// had. So a flag counts as documented only where the catalogue <em>declares</em> it — inside a fenced
    /// block, left of the <c>#</c> that opens the description — which is the same position
    /// <see cref="SkillBlock"/> already keys on. Measured over the file as it stands: 45 declared, 46
    /// mentioned, and the difference is somebody else's flag.</para>
    ///
    /// <para><b>The source side is every flag literal something COMPARES against</b>, which is what makes
    /// this derivable at all and, since T248, what keeps it from reading its own prose. It used to be every
    /// <c>"--x"</c> literal with <c>//</c>-to-end-of-line stripped first — right about comments and
    /// approximate about strings, because a <c>//</c> inside a literal (a URL, a UNC path, a regex) ended
    /// the line for the scan and took any real flag after it with it, silently. Matching the comparison
    /// instead excludes prose by construction: a paragraph may quote a flag all it likes, and what counts
    /// is a literal standing beside <c>Contains</c>, <c>IndexOf</c>, <c>Equals</c>, an equality operator or
    /// an <c>is</c> pattern. (Which is why this paragraph names the operators and does not write one out —
    /// prose is excluded because it is prose about code, not because it may not mention a flag.)</para>
    ///
    /// <para><b>What happens to a flag read a way this does not know</b> — the question narrowing had to
    /// answer, since a pattern of shapes is the hardcoded list this file keeps warning about. It drops out
    /// of <c>accepted</c>, and the <em>second</em> assertion below then fails on it by name: it is declared
    /// in the catalogue and no source appears to read it. The two directions catch each other, so an
    /// unlearned shape is a red build that says which flag and not a check that quietly asserts less. The
    /// gap left is a flag both read a new way and never documented, which is one edit by one author who is
    /// looking at both.</para>
    /// </summary>
    private static void FlagCatalogue() =>
        Repo("every flag the sources accept is declared in dev-flags", FlagCatalogue, DevFlags, "src");

    private const string FlagLiteral = @"""(?<flag>--[a-z][a-z0-9-]*)""";

    // .NET merges repeats of a named group, so one name serves every alternative.
    private static readonly Regex FlagComparison = new(
        $@"\b(?:Contains|IndexOf|Equals|StartsWith|EndsWith)\s*\([^)]*?{FlagLiteral}"
        + $@"|[=!]=\s*{FlagLiteral}"
        + $@"|{FlagLiteral}\s*[=!]="
        + $@"|\bis\s+{FlagLiteral}",
        RegexOptions.Compiled);

    private static void FlagCatalogue(string root)
    {
        string skill = Path.Combine(root, DevFlags);
        string src = Path.Combine(root, "src");
        string[] accepted = (Directory.Exists(src)
                ? Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
                : Array.Empty<string>())
            // Over the code, not the file (T285). Matching the comparison shape is what keeps a flag merely
            // *named* in a paragraph out of this list, and it is not enough on its own: a paragraph that
            // quotes the shape — `Contains("--x")`, written to explain what is matched — is read as a flag
            // the .exe accepts. That is this scan's version of the failure T283's and T284's each had, and
            // it turned up the moment CodeOf existed to be used here. FlagsRead's own contract is unchanged:
            // its fixtures still hand it prose directly, because what they assert is the shape rule.
            .SelectMany(f => FlagsRead(CodeOf(File.ReadAllText(f))))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();

        // Declaration position only: left of the `#`, inside a fence. The prose above and below names
        // flags belonging to other programs, and a check that calls one of those a broken promise is a
        // check somebody turns off.
        var declared = new HashSet<string>(StringComparer.Ordinal);
        bool fenced = false;
        foreach (string line in File.ReadAllLines(skill))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) { fenced = !fenced; continue; }
            if (!fenced) continue;
            int hash = line.IndexOf('#');
            foreach (Match m in Regex.Matches(hash < 0 ? line : line[..hash], @"--[a-z][a-z0-9-]*"))
                declared.Add(m.Value);
        }

        if (!Check("the flag catalogue and the sources are both readable",
                   accepted.Length >= 30 && declared.Count >= 30,
                   $"{accepted.Length} accepted, {declared.Count} declared — the check cannot read what it compares"))
            return;

        string[] undocumented = accepted.Where(f => !declared.Contains(f)).ToArray();
        Check($"every flag the sources accept is declared in dev-flags ({accepted.Length})",
              undocumented.Length == 0,
              $"{string.Join(", ", undocumented)} — the .exe answers to it and the catalogue does not name it");

        string[] promised = declared.Where(f => !accepted.Contains(f, StringComparer.Ordinal))
                                    .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Check($"and every flag it declares is read by something ({declared.Count})", promised.Length == 0,
              $"{string.Join(", ", promised)} — documented, and no source compares against it");
    }

    /// <summary>
    /// Every doc comment is attached to the member it was written about (T312).
    ///
    /// <para>Measured in this repository on the commit that shipped T308: an insertion landed between a
    /// <c>&lt;summary&gt;</c> and its member, so <c>HasExtraAxis</c> arrived carrying two summaries — its
    /// own and the one written for <c>HasOverQuotaMark</c> — while <c>HasOverQuotaMark</c> carried none.
    /// The build was green at 0 warnings and <c>--selftest</c> passed 725 of 725. It was found because the
    /// next task rewrote that region and a person read it.</para>
    ///
    /// <para>This project puts the <em>reasoning</em> in the comments and AGENTS.md makes that a rule, so a
    /// comment on the wrong member is not cosmetic: it is a claim about the wrong code, in the place the
    /// claim is meant to be authoritative, and it reads as deliberate. Nothing else here reads a comment.</para>
    ///
    /// <para>Two directions, one defect. An insertion <b>after</b> the comment leaves a <c>///</c> run with
    /// nothing attached to it; an insertion <b>before</b> the member merges two runs into one, and the
    /// second summary is the giveaway. Neither needs the compiler.</para>
    /// </summary>
    private static void DocCommentsAttached()
    {
        Repo("every doc comment is attached to the member it describes", root =>
        {
            var doubled = new List<string>();
            var orphaned = new List<string>();
            foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs",
                                                             SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(path);
                string name = Path.GetFileName(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal)) continue;

                    // The whole unbroken run, which is what one member's documentation is.
                    int start = i, summaries = 0;
                    while (i < lines.Length && lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal))
                    {
                        // Counted per line: the opening tag of a second block is what an adopted comment
                        // looks like, and <para>/<remarks> inside one block are not that.
                        int at = 0;
                        while ((at = lines[i].IndexOf("<summary>", at, StringComparison.Ordinal)) >= 0)
                        { summaries++; at += 9; }
                        i++;
                    }
                    if (summaries > 1) doubled.Add($"{name}:{start + 1} ({summaries} summaries)");
                    // What follows the run has to be the thing it documents. A blank line — or the end of
                    // the file — means the member it was written for is no longer under it.
                    if (i >= lines.Length || lines[i].Trim().Length == 0)
                        orphaned.Add($"{name}:{start + 1}");
                    i--;   // the outer loop advances
                }
            }
            Check("no member carries a summary written for another one", doubled.Count == 0,
                  $"{string.Join(", ", doubled)} — two summaries in one run means an insertion merged them, " +
                  "so one member has both and the member above it has none (T312)");
            Check("and no doc comment is left with nothing under it", orphaned.Count == 0,
                  $"{string.Join(", ", orphaned)} — a blank line between a /// run and a declaration is a " +
                  "comment about code that has moved away from it");
        }, "src/Cli/SelfTestCli.cs");
    }

    /// <summary>
    /// The owner the list of write call sites never had (T344).
    ///
    /// <para><see cref="ObservingTray"/> below drives the writers somebody listed, which is the stronger
    /// assertion — it proves nothing landed, not that a gate is present — and it has the failure mode its
    /// own doc names: a store it does not drive is a store it promises nothing about. Three had accumulated
    /// behind it. <c>TrayContext.LogResetEvent</c> appended to the reset log on any poll where a window
    /// reset; <c>ProfileStore.DirFor</c> created a directory under the store root on every read; and
    /// <c>ProfileStore.Migrate</c>, reached from startup, <c>File.Move</c>d the user's files. None was
    /// reachable from a check without a tray, and the fourth would have gone the same way.</para>
    ///
    /// <para>So this asks the source instead, the way T293 and T297 ask theirs. <b>Both halves of the scope
    /// are derived, never listed.</b> A file is in it when it can address the store root at all — it names
    /// <c>Settings.DataDir</c>, <c>SpecialFolder.LocalApplicationData</c>, or one of
    /// <c>ProfileStore</c>'s path resolvers — because that is the only way to be a store, so a store added
    /// in a folder nobody thought of is covered on the day it resolves its path. A call is in it when it
    /// mutates a file or a directory. What remains is the exemption table, and it is the one thing here
    /// that <em>is</em> a list: an entry names a method and says what makes it safe, so an exemption is a
    /// decision somebody wrote down rather than a call nobody looked at.</para>
    ///
    /// <para>The table is asserted in both directions. An exemption matching nothing is as red as an
    /// ungated write: a method that was renamed away takes its reason with it, and what is left is a line
    /// excusing a call site that no longer exists while the one that replaced it goes unread.</para>
    /// </summary>
    private static void ObservingGateCallSites()
    {
        // File, method, and what makes the write safe. Each one was checked against its callers when it
        // was written down; a caller added later that skips the gate is the case this cannot see, which is
        // why the reasons name the caller rather than saying "private".
        (string File, string Method, string Why)[] exempt =
        {
            ("HeaderProbe.cs", "Clear",
             "a fixture rebuild, on AccountFixture's invented keys — it deletes the fixture's own store"),
            ("UsageHistory.cs", "Clear",
             "the same fixture rebuild, and the same invented keys"),
            ("HeaderProbe.cs", "Trim",
             "reached only from Record, which gates before it writes the line this then trims"),
            ("UsageHistory.cs", "PruneIfStale",
             "reached only from Append, which gates"),
            ("ContextHistory.cs", "PruneIfStale",
             "reached only from Record, which gates"),
            ("HourlyUsage.cs", "WriteAll",
             "the fold's writer: Fold gates before calling it, and --selftest's own fixture is the only " +
             "other caller"),
        };

        Repo("every write into the store consults the observing gate, or is exempt by name", root =>
        {
            string[] mutators =
            {
                "File.WriteAll", "File.AppendAll", "File.Move(", "File.Delete(", "File.Copy(",
                "File.Create(", "File.Replace(", "Directory.CreateDirectory(", "Directory.Move(",
                "Directory.Delete(",
            };
            // What makes a file able to write into the user's store at all. Nothing else can be one.
            string[] addressesStore =
            {
                "Settings.DataDir", "SpecialFolder.LocalApplicationData", "DirFor(", "PathFor(",
            };

            // Every mutation, in the one form that can be counted at runtime (T356).
            string[] viaStoreFile =
            {
                "StoreFile.WriteAll", "StoreFile.AppendAll", "StoreFile.Move(", "StoreFile.Delete(",
                "StoreFile.CreateDirectory(",
            };

            var ungated = new List<string>();
            var raw = new List<string>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs",
                                                             SearchOption.AllDirectories))
            {
                // The CLI is not a tray and never becomes one: --selftest, the previews and the captures
                // are their own processes, and every file they write is one the command line asked for.
                if (Path.GetFullPath(path).Contains(Path.Combine("src", "Cli") + Path.DirectorySeparatorChar,
                                                    StringComparison.OrdinalIgnoreCase))
                    continue;
                string text = File.ReadAllText(path);
                if (!addressesStore.Any(k => text.Contains(k, StringComparison.Ordinal))) continue;

                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                string file = Path.GetFileName(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].TrimStart();
                    if (t.StartsWith("//", StringComparison.Ordinal)) continue;

                    // `StoreFile.WriteAllText` contains `File.WriteAllText`, and `OutFile.Create`
                    // contains `File.Create`, so raw detection has to be blind to both prefixes or every
                    // converted call reports itself. Masked rather than pattern-matched: the two names
                    // are the only wrappers, and a third would have to be added here deliberately.
                    string masked = t.Replace("StoreFile.", "Store_", StringComparison.Ordinal)
                                     .Replace("OutFile.", "Out_", StringComparison.Ordinal);
                    bool viaStore = viaStoreFile.Any(m => t.Contains(m, StringComparison.Ordinal));
                    bool isRaw = mutators.Any(m => masked.Contains(m, StringComparison.Ordinal));
                    if (!viaStore && !isRaw) continue;
                    if (isRaw) raw.Add($"{file}:{i + 1}");

                    int decl = DeclaringMember(lines, i);
                    // From the declaration down to the write: a gate below the write gates nothing.
                    string body = decl < 0 ? "" : string.Join("\n", lines[decl..(i + 1)]);
                    if (body.Contains("Observing", StringComparison.Ordinal)) continue;

                    string method = decl < 0 ? "(no enclosing member)" : MemberName(lines[decl]);
                    if (exempt.Any(e => e.File == file && e.Method == method)) { used.Add(file + ":" + method); continue; }
                    ungated.Add($"{file}:{i + 1} in {method}");
                }
            }

            // T356. A raw File/Directory mutation in one of these files is invisible to the write
            // counter, so the runtime assertion would pass while the write happened. The counter is only
            // as good as the layer being the only way through — that is asserted here, not assumed.
            Check("every store mutation goes through StoreFile, so it can be counted", raw.Count == 0,
                  $"{string.Join(", ", raw)} — a File or Directory call that bypasses StoreFile is a "
                  + "write the observing counter cannot see, which makes a green run mean less than it "
                  + "says (T356). Use StoreFile, whose operations are the same minus the blind spot.");

            Check("no write into the store skips the gate", ungated.Count == 0,
                  $"{string.Join(", ", ungated)} — each mutates a file under %LocalAppData%\\ClaudeTray "
                  + "without consulting ProfileStore.Observing, so a tray launched with --second-tray "
                  + "writes it into the user's own store (T344). Gate it, or add it to the table beside "
                  + "this check with the reason it is safe.");

            string[] stale = exempt.Where(e => !used.Contains(e.File + ":" + e.Method))
                                   .Select(e => e.File + ":" + e.Method).ToArray();
            Check("and every exemption still names a write that exists", stale.Length == 0,
                  $"{string.Join(", ", stale)} — an exemption matching nothing is a reason attached to a "
                  + "call site that has gone, while whatever replaced it is read by nobody");
        }, "src");
    }

    /// <summary>
    /// Only the tray asks to migrate the store (T357).
    ///
    /// <para>Adopting the profile is the first thing <c>Program.Main</c> does, above every flag it
    /// dispatches — so the migration ran against the user's real store while <c>ProfileStore.Observing</c>
    /// was still false, and the gate T344 put on it never applied to a single dev flag. The write is now
    /// asked for rather than assumed: <c>SetMonitored</c> migrates only when the caller says it may.</para>
    ///
    /// <para><b>Asserted on the source, because the runtime version cannot fail here.</b> On any machine
    /// that has already migrated there is nothing left to move, so a counter would read zero whether the
    /// argument were honoured or not — the check would pass for the wrong reason on every developer's
    /// machine and CI alike. What is checkable is who asks: exactly one call site, and it is the tray's
    /// own poll refresh, where the process is unambiguously the tray.</para>
    /// </summary>
    private static void OnlyTheTrayMigrates() =>
        Repo("only the tray asks the store to migrate", root =>
        {
            var askers = new List<string>();
            foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs",
                                                             SearchOption.AllDirectories))
            {
                // A scanner that names the string it looks for reports itself — the same reason
                // StoreRootSpelledOnce skips the suite. By stem, since T381 made the suite six files:
                // keyed on the filename, this went red on the split rather than on a defect.
                if (IsSuite(path)) continue;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].TrimStart();
                    if (t.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (t.Contains("mayMigrate: true", StringComparison.Ordinal))
                        askers.Add($"{Path.GetFileName(path)}:{i + 1}");
                }
            }
            Check("exactly one call site asks to migrate, and it is the tray's",
                  askers.Count == 1 && askers[0].StartsWith("TrayContext.cs", StringComparison.Ordinal),
                  $"{(askers.Count == 0 ? "(none)" : string.Join(", ", askers))} — the store is migrated "
                  + "where the process is known to be the tray, and nowhere else. A flag that asks for it "
                  + "moves the user's files before the observing gate exists to stop it (T357).");
        }, "src");

    /// <summary>
    /// Where the store lives is one fact, spelled in one place (T354).
    ///
    /// <para><c>TrayContext.LogResetEvent</c> composed <c>%LocalAppData%\ClaudeTray</c> itself and
    /// appended <c>reset-events.log</c> there, while <c>ProfileStore.PerProfileFiles</c> had listed that
    /// file as one profile's own since profiles existed. The two never agreed and nothing could notice:
    /// the writer was right about the bytes it wrote and the list was right about where they belonged,
    /// and no check compares a path to a declaration. What is checkable is the second speller.</para>
    ///
    /// <para>So: the store root is composed in <c>Settings.DataDir</c> and nowhere else. Two shared
    /// caches were also re-spelling it — legitimately flat, since they are keyed by absolute path and
    /// are deliberately not per-profile, but that is a fact about which directory, not about how to find
    /// it. A path built from the root by hand is one <c>PathFor</c> nobody called.</para>
    ///
    /// <para>Derived, with no exemption table: the display substitution on the System page names
    /// <c>"%LocalAppData%"</c> and not the store folder, so it is not a composition and the scan never
    /// sees it. A list here would have been the thing T344 spent a task removing.</para>
    /// </summary>
    private static void StoreRootSpelledOnce()
    {
        Repo("the store root is composed in one place and nowhere else", root =>
        {
            var spellers = new List<string>();
            foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs",
                                                             SearchOption.AllDirectories))
            {
                // The check itself names both halves in its own prose, and a scanner that reads its own
                // source is a scanner that reports itself. By stem — the suite is six files (T381).
                if (IsSuite(path)) continue;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].TrimStart();
                    if (t.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!t.Contains("SpecialFolder.LocalApplicationData", StringComparison.Ordinal)) continue;
                    // The folder name may sit on this line or on the next — `Path.Combine` wraps either
                    // way, and which it is is a formatting accident, not a difference.
                    string pair = lines[i] + (i + 1 < lines.Length ? lines[i + 1] : "");
                    if (pair.Contains("\"ClaudeTray\"", StringComparison.Ordinal))
                        spellers.Add($"{Path.GetFileName(path)}:{i + 1}");
                }
            }
            Check("exactly one place joins %LocalAppData% to the store folder",
                  spellers.Count == 1 && spellers[0].StartsWith("Settings.cs", StringComparison.Ordinal),
                  $"{(spellers.Count == 0 ? "(none)" : string.Join(", ", spellers))} — a second speller is a "
                  + "path that cannot be per-profile by construction, which is how a store declared one "
                  + "way came to be written another (T354). Build it from Settings.DataDir, or from "
                  + "ProfileStore.PathFor where it belongs to a profile.");
        }, "src");
    }

    /// <summary>
    /// An observing tray adds nothing to the user's files (T239) — asserted against the real
    /// <c>%LocalAppData%\ClaudeTray</c>, because that is the thing being promised.
    ///
    /// <para>Every write entry point is driven with arguments that WOULD write, and the whole tree is
    /// compared before and after: not "the gate is present" — which is what reading the source would
    /// tell you — but "nothing landed", which is the property. A store added later without the gate
    /// fails here the moment this drives it, and a store added later that this does not drive is the
    /// hole the doc comment on <c>ProfileStore.Observing</c> names.</para>
    ///
    /// <para>Runs last for the same reason as the section above: <c>Observe()</c> is one-way.</para>
    /// </summary>
    private static void ObservingTray()
    {
        string data = Settings.DataDir;
        // Whether the tree could be READ — the one state that makes the comparison below vacuous, and so
        // the one the precondition asks about. An empty tree is not that state: a machine that has never
        // run the tray has nothing to fingerprint, and every file the calls below would create still
        // shows up in the second snapshot and is still named. Asking for a non-empty baseline instead
        // failed on exactly that machine — the runner's %LocalAppData%\ClaudeTray is created by the
        // `stores` section above and emptied again by its own cleanup, leaving a directory that exists
        // and holds nothing, which is a fine state to promise "nothing landed" about.
        bool readable = true;
        // A fingerprint of every file the app owns: path, length and write time. Taken before the switch
        // is thrown, so the baseline is the state a check run must leave behind untouched.
        Dictionary<string, (long Len, DateTime When)> Snapshot()
        {
            var map = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(data))
                    foreach (string f in Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(f);
                        map[f] = (fi.Length, fi.LastWriteTimeUtc);
                    }
            }
            catch { readable = false; }
            return map;
        }

        Dictionary<string, (long Len, DateTime When)> before = Snapshot();
        Check("the store tree can be read at all, or the comparison below asserts nothing", readable);

        // Another ClaudeTray on this machine writes the same tree on its own timer, so the comparison
        // below cannot attribute what it finds (T356). The counter can — it is this process's own
        // bookkeeping — so it carries the assertion and the diff becomes the corroboration.
        int otherTrays = 0;
        try
        {
            int self = Environment.ProcessId;
            otherTrays = System.Diagnostics.Process.GetProcessesByName("ClaudeTray").Count(p => p.Id != self);
        }
        catch { /* a denied process list is not a reason to fail a store check */ }

        ProfileStore.Observe();
        Check("observing is one-way and the environment goes with it (T231's sampling, T239's promise)",
              ProfileStore.Observing && EnvironmentProfile.IsSampled);

        // From here on every mutation this process performs is counted at the point it happens (T356),
        // so the promise — *this process wrote nothing* — is asserted directly instead of inferred from
        // a tree two processes share. Reset rather than read-and-compare: whatever startup did before
        // the gate was thrown is T357's, not this section's.
        StoreFile.ResetWrites();

        // Real keys and real arguments: the point is that these calls would otherwise land on the files
        // fingerprinted above. `Monitored` is this machine's own profile key, which is the one a second
        // tray polls and the one a check run would have appended to.
        string key = ProfileStore.Monitored;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        UsageHistory.Append(key, now, 0.42, now + 3600, 0.21, now + 86400, 0.1, now + 7200);
        HourlyUsage.Fold(key, new List<UsageSample>
        {
            new(now - 7200, 0.10, 0, 0.05, 0, null, 0),
            new(now - 3600, 0.20, 0, 0.10, 0, null, 0),
            new(now,        0.30, 0, 0.15, 0, null, 0),
        }, now);
        ContextNudges.Mark(key, "selftest-observing", DateTime.UtcNow);
        // T305, and the store that had been missing from this list. `HeaderProbe.Record` writes a file and
        // consulted nothing, so an observing tray appended to `header-probe.jsonl` on a new header shape —
        // and `Trim` then rewrote the whole file, which is a promise broken twice. The shape is invented so
        // the call cannot be a no-op against whatever this machine last recorded: a drive that writes
        // nothing because nothing changed is a drive that asserts nothing.
        Check("an observing tray records no probe reading",
              !HeaderProbe.Record(key, now, new Dictionary<string, string>
              {
                  ["anthropic-ratelimit-unified-status"] = "selftest-observing",
                  ["anthropic-ratelimit-unified-5h-status"] = "allowed",
              }),
              "Record answered true — it wrote a line to a store this process promised only to read");
        // The two SHARED caches, which the per-profile list above does not reach and which an end-to-end
        // run caught changing after the first five gates were in (T239). Driven through the scan and the
        // usage pass, because their writers are private: what is being asserted is that a scan an
        // observing tray runs leaves the cache alone, not that a method exists.
        ContextScanner.Scan(DateTime.UtcNow);
        ContextUsage.Compute(DateTime.UtcNow);
        // The third shared cache (T327). Driven against the real tree and the real cache path for the
        // same reason as the two above: a store this section does not drive is a store nothing here
        // promises anything about.
        SessionIndex.Load();
        new Settings().Save();
        var settings = new Settings();
        EnvironmentProfile.Adopt(settings, @"C:\Users\nobody\.claude-observing");
        EnvironmentProfile.Drain();

        // The promise itself, counted rather than inferred. This one always runs: another tray cannot
        // increment this process's counter, so there is nothing here for a stray poll to confuse.
        Check($"this process performed no store write while observing ({StoreFile.Writes})",
              StoreFile.Writes == 0,
              $"{StoreFile.Writes} mutation(s) went through StoreFile after the gate was thrown — a "
              + "writer that consults nothing, which is what T239 promises cannot exist (T356)");

        Dictionary<string, (long Len, DateTime When)> after = Snapshot();
        var moved = new List<string>();
        foreach (KeyValuePair<string, (long Len, DateTime When)> kv in after)
            if (!before.TryGetValue(kv.Key, out (long Len, DateTime When) was) || was != kv.Value)
                moved.Add(Path.GetFileName(kv.Key));
        bool deleted = !before.Keys.All(after.ContainsKey);

        // And the corroboration: nothing in the tree moved either. It catches a write that bypassed
        // StoreFile entirely, which the counter by construction cannot — but only where this process is
        // the only writer. With another tray alive it is stood down by name rather than left to fail on
        // that tray's poll: a red a re-run clears teaches re-running, and this is the one check where
        // that habit is most expensive (T356).
        if (otherTrays > 0)
            Skip("no file under %LocalAppData%\\ClaudeTray was created or changed",
                 $"{otherTrays} other ClaudeTray process(es) write this tree on their own timer, so a "
                 + "change here cannot be attributed to this run — the counted assertion above is the "
                 + "one that answers the promise");
        else
        {
            // `readable` covers the second snapshot too: a tree that became unreadable between the two
            // reads produces an empty `after`, over which both comparisons would otherwise pass saying
            // nothing.
            Check("no file under %LocalAppData%\\ClaudeTray was created or changed"
                  + (moved.Count > 0 ? " — moved: " + string.Join(", ", moved.Distinct()) : ""),
                  readable && moved.Count == 0);
            Check("and nothing was deleted either", readable && !deleted);
        }
    }

    /// <summary>
    /// Every case the interaction script declares is named in <c>AGENTS.md</c> (T360).
    ///
    /// <para>The rule is already in that document's own text — <em>"listing three is how two stayed
    /// script-only (T201)"</em> — and two cases really did exist that nobody knew to run. Nothing checked
    /// it, so the rule held exactly as long as each author happened to read it. T359 added a seventh case
    /// and the whole suite stayed green with the document still saying six; it was documented because the
    /// rule was followed, which is the guarantee T201 says is not one.</para>
    ///
    /// <para><b>Both directions, and the names only.</b> A case in the script and not the document is one
    /// nobody runs — the original defect. A name in the document and not the script is a case somebody
    /// deleted and a reader will go looking for. What is <em>not</em> asserted is how much prose each gets:
    /// <c>Panes</c> and <c>Names</c> deliberately share a bullet, and AGENTS.md sits one line under its own
    /// ceiling, so a check demanding a paragraph each would one day demand a line the budget refuses.</para>
    /// </summary>
    private static void InteractionCasesDocumented() =>
        Repo("every interaction case the script declares is named in AGENTS.md", root =>
        {
            string script = File.ReadAllText(Path.Combine(root, "scripts/Check-Interaction.ps1"));
            Match set = Regex.Match(script, @"\[ValidateSet\(([^)]*)\)\]");
            string[] declared = Regex.Matches(set.Groups[1].Value, @"'([A-Za-z]+)'")
                                     .Select(m => m.Groups[1].Value)
                                     .Where(n => n != "All")
                                     .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            if (declared.Length == 0) { Check("the script declares a case set at all", false, "no ValidateSet found"); return; }

            // The section, not the file: `-Case` and these names appear nowhere else, but bounding it
            // keeps the check about the passage a reader of that section is relying on.
            string agents = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
            int from = agents.IndexOf("## Interaction verification", StringComparison.Ordinal);
            int to = from < 0 ? -1 : agents.IndexOf("\n## ", from + 4, StringComparison.Ordinal);
            string section = from < 0 ? "" : (to < 0 ? agents[from..] : agents[from..to]);
            if (section.Length == 0) { Check("AGENTS.md still has an interaction section", false, "no '## Interaction verification' heading"); return; }

            string[] inUsage = Regex.Match(section, @"\[-Case ([A-Za-z|]+)\]") is { Success: true } u
                ? u.Groups[1].Value.Split('|').OrderBy(n => n, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
            Check($"the usage line lists exactly the cases the script accepts ({declared.Length})",
                  inUsage.SequenceEqual(declared, StringComparer.Ordinal),
                  $"script: {string.Join("|", declared)} — AGENTS.md: {string.Join("|", inUsage)}. A case "
                  + "missing here is a case nobody knows to run, which is the defect T201 named and left "
                  + "to prose (T360).");

            // Bold, because that is how the section introduces each one — and a name in a sentence about
            // something else would otherwise count as documentation.
            var bold = Regex.Matches(section, @"\*\*([A-Za-z]+)\*\*").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            string[] undescribed = declared.Where(n => !bold.Contains(n)).ToArray();
            Check("and each of them is introduced in the prose below it",
                  undescribed.Length == 0,
                  $"{string.Join(", ", undescribed)} — listed in the usage block and described nowhere, so "
                  + "a reader learns the flag exists and not what it drives (T360).");

            // T364: the same question of the script's own comment-based help, which is what `Get-Help`
            // prints and the first thing a reader of the file meets. Checking only AGENTS.md let the
            // header keep describing six cases after T359 added the seventh.
            string help = script[..(script.IndexOf("#>", StringComparison.Ordinal) is var end && end > 0
                                    ? end : script.Length)];
            string[] inHelp = Regex.Matches(help, @"^\s*-Case ([A-Za-z]+)\s", RegexOptions.Multiline)
                                   .Select(m => m.Groups[1].Value)
                                   .Distinct(StringComparer.Ordinal)
                                   .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Check($"the script's own help gives every case a bullet ({declared.Length})",
                  inHelp.SequenceEqual(declared, StringComparer.Ordinal),
                  $"ValidateSet: {string.Join("|", declared)} — help: {string.Join("|", inHelp)}. The "
                  + "header is what Get-Help prints, so a case missing from it is undiscoverable from "
                  + "the file that implements it (T364).");

            // And no spelled-out count beside the list, because that is the one claim editing the list
            // does not update — it said "Three" while the ValidateSet held seven.
            Match counted = Regex.Match(help, @"\b(One|Two|Three|Four|Five|Six|Seven|Eight|Nine)\s+cases\b",
                                        RegexOptions.IgnoreCase);
            Check("and does not spell out how many there are",
                  !counted.Success,
                  $"the help says \"{counted.Value}\" — a number in prose that nothing derives is wrong "
                  + "one case later, which is how this defect arrived (T364).");
        }, "scripts/Check-Interaction.ps1", "AGENTS.md");

    /// <summary>
    /// Every id <c>Check-Interaction.ps1</c> looks a control up by, because that lookup is a <em>string</em>
    /// and no compiler checks it. Renaming <c>StatusText</c> while doing T192 broke the one behind T166's
    /// "the status line must never be observed at all" — and a lookup that finds nothing makes that
    /// assertion pass by seeing nothing, which is §XX.2's defect exactly.
    ///
    /// <para><b>Derived from the script, not remembered (T203).</b> T192 wrote the fifteen ids out by hand,
    /// and a hand list is right on the day it is read off and decays silently after: by the time this was
    /// written it had already missed three (<c>BrowseButton</c>, <c>ProfileAddButton</c>,
    /// <c>ProfileRemoveButton</c>, all added with T196's row trio), and a list that is short looks exactly
    /// like a list that is complete. The uniqueness half above has never had that problem because it
    /// reflects over every <c>IComponentConnector</c>; this is the same move against a text file.</para>
    ///
    /// <para><b>Three shapes carry a literal id</b>, not one — deriving only from <c>ById</c> would have
    /// reproduced the very gap this fixes, since the three missing ones arrive as table rows:
    /// <c>ById</c>/<c>ByIdNow</c>/<c>Assert-Name</c> called with a quoted id, and <c>Id = '…'</c> in a
    /// hashtable the case then walks. A lookup whose id is a <em>variable</em> is invisible to any regex,
    /// so the ids built at runtime (<c>"Used$sfx"</c> and its two siblings) stay explicit — and the check
    /// says which kind it is asserting, because the two have different failure modes.</para>
    /// </summary>
    private static void InteractionIds(Dictionary<string, List<string>> owners) =>
        Repo("every id the interaction check drives still exists", root => InteractionIds(root, owners),
             "scripts/Check-Interaction.ps1");

    private static void InteractionIds(string root, Dictionary<string, List<string>> owners)
    {
        string script = Path.Combine(root, "scripts/Check-Interaction.ps1");
        string text = File.ReadAllText(script);
        string[] derived = System.Text.RegularExpressions.Regex
            .Matches(text, @"(?:ById|ByIdNow|Assert-Name)\s+\$\w+\s+'([A-Za-z]\w*)'|Id\s*=\s*'([A-Za-z]\w*)'")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        // The guard on the precondition, not on a weaker form of the claim: a pattern that stopped matching
        // yields an empty set, and "none of zero ids is gone" is a green tick over nothing.
        if (!Check("the interaction script's id lookups are found by pattern", derived.Length >= 12,
                   $"matched {derived.Length} in {Path.GetFileName(script)} — the lookup syntax changed " +
                   "and this check is now asserting almost nothing"))
            return;

        string[] gone = derived.Where(id => !owners.ContainsKey(id)).ToArray();
        Check($"every id the interaction check drives still exists ({derived.Length}, derived)",
              gone.Length == 0,
              gone.Length == 0 ? "" : $"{gone.Length} gone — {string.Join(", ", gone)}: renamed without " +
                                      "updating scripts\\Check-Interaction.ps1, whose lookups are strings");

        // The other kind: assembled per pane suffix, so no literal exists to match. Both halves are
        // asserted — that the control is still named that, and that the script still builds it that way,
        // since a rename of the *pattern* would leave this list pointing at ids nobody looks up any more.
        string[] composed = { "UsedS", "UsedW", "ResetS", "ResetW", "LiveHeadS", "LiveHeadW" };
        string[] lostControl = composed.Where(id => !owners.ContainsKey(id)).ToArray();
        Check($"every id it builds per pane still exists ({composed.Length}, explicit)",
              lostControl.Length == 0,
              lostControl.Length == 0 ? "" : $"{lostControl.Length} gone — {string.Join(", ", lostControl)}");

        string[] lostPattern = new[] { "Used", "Reset", "LiveHead" }
            .Where(p => !text.Contains($"\"{p}$sfx\"", StringComparison.Ordinal)).ToArray();
        Check("and the script still assembles them from $sfx", lostPattern.Length == 0,
              lostPattern.Length == 0 ? "" : $"{string.Join(", ", lostPattern)} no longer built that way — " +
                                             "the explicit list above is stale and asserts nothing");
    }

    /// <summary>
    /// The two documents a person outside this repository actually reads (T250): <c>README.md</c> and the
    /// published site. The user-facing-surface gate names three surfaces a shipped feature owes, and only
    /// <c>lang\*.json</c> was held to it — T185 fails a key that reached one language file. These two were
    /// maintained on trust, by the same hand in the same commit, which is exactly the pattern that drifts.
    ///
    /// <para><b>What is deliberately not asserted, and this is what §XXII.10 left to settle.</b> The gate
    /// asks the two to be *consistent in wording*, and a check demanding that would fail on every rewrite —
    /// the failure mode that gets a check switched off. The site's headings are marketing copy and the
    /// README's are not, on purpose. So the wording is left alone and what is checked is what has a right
    /// answer: the <b>assets</b> both files point at, and the <b>identifier</b> both files quote.</para>
    ///
    /// <para>Three ways they go wrong, all silent. A screenshot renamed leaves a broken image on a page
    /// nobody loads until a stranger does. A screenshot that neither file shows is one nobody re-takes when
    /// the window moves, so it rots into a picture of an app that no longer exists. And the winget id is a
    /// string a user types: spelled two ways it sends somebody to a package that is not there, and it is
    /// quoted in six places across the README, the site and the manifests.</para>
    ///
    /// <para><b>Where the page is.</b> It used to be one hand-written <c>docs\index.html</c>, and this check
    /// read that file. The site is now a prerendered workspace under <c>site\</c>, so the page's asset
    /// references live in its copy modules (<c>site\src\lib\site-content.ts</c> and <c>features.ts</c>) and
    /// its screenshots in <c>site\public\shots</c>. Those are what is read here: the copy is the page's
    /// source of truth, so a screenshot the copy no longer names is one no page shows. The built
    /// <c>site\dist</c> is deliberately not read — it is gitignored, so on a clean clone this check would
    /// have nothing to look at, and the site's own <c>npm test</c> already asserts the built output.</para>
    /// </summary>
    private static void UserSurfaces(string root)
    {
        string readme = Path.Combine(root, "README.md");
        string publicDir = Path.Combine(root, "site", "public");
        string shotsDir = Path.Combine(publicDir, "shots");
        string readmeText = File.ReadAllText(readme);

        // Both copy modules, because the depth pages carry their own figures: reading only the landing
        // copy would call five screenshots orphans that a page does show.
        string siteText = string.Concat(
            new[] { "site-content.ts", "features.ts" }
                .Select(n => Path.Combine(root, "site", "src", "lib", n))
                .Where(File.Exists)
                .Select(File.ReadAllText));

        // The README's are markdown and inline <img> with repo-relative paths.
        static string[] ReadmeAssets(string text) =>
            Regex.Matches(text, $@"(?:src|href|content)=""([^"":]+\.(?:png|gif|jpg|svg))""")
                 .Select(m => m.Groups[1].Value)
                 .Concat(Regex.Matches(text, @"!\[[^\]]*\]\(([^)]+\.(?:png|gif|jpg|svg))\)")
                              .Select(m => m.Groups[1].Value))
                 .Where(p => !p.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .OrderBy(p => p, StringComparer.Ordinal).ToArray();

        // The site's are string literals carrying the base prefix GitHub Pages derives from the repository
        // name, stripped back to the path under site\public.
        static string[] SiteAssets(string text) =>
            Regex.Matches(text, @"""/claude-tray/(shots/[^""]+\.(?:png|gif|jpg|svg))""")
                 .Select(m => m.Groups[1].Value)
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .OrderBy(p => p, StringComparer.Ordinal).ToArray();

        string[] fromReadme = ReadmeAssets(readmeText);
        string[] fromSite = SiteAssets(siteText);
        if (!Check("both documents still reference their screenshots",
                   fromReadme.Length >= 8 && fromSite.Length >= 8,
                   $"{fromReadme.Length} in the README, {fromSite.Length} on the page — " +
                   "the check cannot read what it compares"))
            return;

        string[] brokenReadme = fromReadme.Where(p => !File.Exists(Path.Combine(root, p))).ToArray();
        Check($"every image the README points at exists ({fromReadme.Length})", brokenReadme.Length == 0,
              $"{string.Join(", ", brokenReadme)} — a broken image in the first thing anybody reads");

        string[] brokenSite = fromSite.Where(p => !File.Exists(Path.Combine(publicDir, p))).ToArray();
        Check($"every image the published page points at exists ({fromSite.Length})", brokenSite.Length == 0,
              $"{string.Join(", ", brokenSite)} — broken on a page nobody loads until a stranger does");

        // Both lists reduced to a bare file name: the README writes site/public/shots/x.png and the copy
        // writes /claude-tray/shots/x.png, and the question is only whether *something* still shows it.
        var shown = new HashSet<string>(
            fromSite.Concat(fromReadme).Select(p => Path.GetFileName(p)),
            StringComparer.OrdinalIgnoreCase);
        string[] orphans = Directory.Exists(shotsDir)
            ? Directory.GetFiles(shotsDir, "*.png").Select(f => Path.GetFileName(f))
                       .Where(f => !shown.Contains(f))
                       .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        Check("and every screenshot in site\\public\\shots is shown by one of them", orphans.Length == 0,
              $"{string.Join(", ", orphans)} — shown nowhere, so nobody re-takes it when the window moves");

        // An identifier, not prose: a user types it, and two spellings send one of them nowhere.
        string[] ids = Regex.Matches(readmeText + siteText, @"alegauss\.[A-Za-z]+")
            .Select(m => m.Value).Distinct(StringComparer.Ordinal)
            .Where(v => !v.Equals("alegauss.github", StringComparison.Ordinal))
            .OrderBy(v => v, StringComparer.Ordinal).ToArray();
        Check("the README and the page quote one winget id", ids.Length == 1,
              $"{string.Join(", ", ids)} — one of these installs nothing");

        string manifest = Path.Combine(root, "build", "winget", "alegauss.ClaudeCodeTray.yaml");
        Check("and it is the one the manifest publishes",
              ids.Length == 1 && File.Exists(Path.Combine(root, "build", "winget", $"{ids[0]}.yaml")),
              $"{(ids.Length == 1 ? ids[0] : "several")} names no manifest beside {Path.GetFileName(manifest)}");
    }

    /// <summary>Whether a family has a real glyph for every codepoint in <paramref name="text"/>. Asked of
    /// the typeface, not of a rendering: a missing glyph draws a box, which is ink like any other.</summary>
    private static bool Maps(System.Windows.Media.FontFamily family, string text)
    {
        foreach (System.Windows.Media.Typeface tf in family.GetTypefaces())
        {
            if (!tf.TryGetGlyphTypeface(out System.Windows.Media.GlyphTypeface? gt)) continue;
            bool all = true;
            for (int i = 0; i < text.Length && all;)
            {
                int cp = char.ConvertToUtf32(text, i);
                i += char.IsSurrogatePair(text, i) ? 2 : 1;
                all = gt.CharacterToGlyphMap.ContainsKey(cp);
            }
            if (all) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- Block N: what "9 projects" counts

    /// <summary>
    /// The types that put a number in front of a reader (T216). A list, unlike the formatters on them,
    /// because there is no property of a type that says "this one is read by a user" — but it is the
    /// short list, and it is the one thing a new surface has to be added to. The rule they all keep is
    /// <see cref="Nums"/>; dates are outside it and stay in the display language.
    /// </summary>
    private static readonly Type[] Surfaces =
    {
        typeof(StatisticsPage),   // the charts, the pace report, the method note
        typeof(ContextPage),      // the gauge caption, the simulation banner, the scan footer
        typeof(ContextText),      // file sizes, and the tray's context nudge
        typeof(SettingsPage),     // the poll cadence, its cost estimate, the extra-usage share
        typeof(TokenEstimate),    // "≈4.9k", on every one of the above
        typeof(SessionListRow),   // a conversation's length, in the Sessions list
        typeof(Nums),             // the rule itself
    };

    // ---------------------------------------------------------------- Blocks K/W: the slug encoding
}
