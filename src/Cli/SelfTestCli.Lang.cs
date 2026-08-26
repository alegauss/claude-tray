using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeTray;

/// <summary>
/// Part of <see cref="SelfTestCli"/> — five language files, one key set, and the words on top of it.
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
    /// The rule <c>lang\*.json</c> exists to keep: a user-visible string is in all five files or in none.
    /// A key that lands only in <c>en</c> falls back silently (<see cref="L.T(string)"/>), so the app reads
    /// correctly in English and stays quietly untranslated everywhere else until somebody opens that screen
    /// in that language — and the stated verification was <c>--lang &lt;code&gt;</c>, which is a person
    /// remembering. Block AE put nineteen keys into five files by hand and ran it for three languages.
    ///
    /// <para>Placeholders are compared too, because <c>{0}</c> present in one language and absent in another
    /// is a formatted string that silently drops a number, and no comparison of key <em>sets</em> sees it.
    /// Every failure names the offending keys rather than counting them: a count is something people learn
    /// to live with.</para>
    ///
    /// <para><b>Where this stops.</b> Whether the Portuguese reads well is not something an assertion can
    /// hold, and this does not pretend to — it is a parity check, not a translation-quality one. It also
    /// cannot see a string hardcoded in XAML, which has no key to be missing.</para>
    /// </summary>
    private static void Translations()
    {
        IReadOnlyDictionary<string, string> en = L.Strings("en");

        // The guard first: a load that silently yielded nothing — a renamed resource, a parse error — would
        // make every comparison below pass over an empty set.
        if (!Check($"the base table loads ({en.Count} keys)", en.Count > 0,
                   "en.json parsed to no keys at all, so nothing below would have compared anything"))
            return;

        foreach (string code in L.Codes)
        {
            if (code == "en") continue;
            IReadOnlyDictionary<string, string> t = L.Strings(code);
            if (!Check($"{code} loads ({t.Count} keys)", t.Count > 0,
                       $"lang\\{code}.json parsed to no keys, and every key would fall back to English"))
                continue;

            string[] missing = Keys(en, k => !t.ContainsKey(k));
            Check($"{code} translates every key en has", missing.Length == 0,
                  Named(missing, "reaching only en"));

            string[] orphan = Keys(t, k => !en.ContainsKey(k));
            Check($"{code} carries no key en does not", orphan.Length == 0,
                  Named(orphan, $"existing in {code} alone, so nothing reads them"));

            string[] slipped = Keys(en, k => t.TryGetValue(k, out string? s) && Holes(en[k]) != Holes(s));
            Check($"{code} keeps every placeholder en has", slipped.Length == 0,
                  Named(slipped, "differing in their {0}-style holes, so a number is dropped or misplaced"));
        }

        Plurals(en);
        NoParenthesisedPlural();
    }

    /// <summary>
    /// The shape <c>L.N</c> replaced, refused everywhere rather than merely cleaned up once (T378).
    ///
    /// <para><b>Why this is a check and not a convention.</b> <c>(s)</c> was never a decision anybody
    /// made — six strings drifted into it over four tasks while the <c>.one</c>/<c>.many</c> pairs for
    /// doing it properly already existed three keys away. Nothing refused the next one, so the cleanup
    /// was a cleanup: a thing that happens twice. Zero remain today, which is exactly when adopting the
    /// rule costs nothing.</para>
    ///
    /// <para><b>The shape, not a list of three.</b> A parenthesised suffix of one to three letters glued
    /// to a word — <c>regra(s)</c>, <c>item(ns)</c>, <c>entry(ies)</c>, <c>élément(s)</c> — because the
    /// four this repository actually used are four of a family, and a check naming only them is one the
    /// fifth walks past. A parenthetical with a space in front of it is prose and is left alone.</para>
    ///
    /// <para><b>Every language, not just <c>en</c>.</b> <c>regra(s)</c> reaching only the Portuguese file
    /// is exactly as wrong and would otherwise be invisible: the key-set and placeholder comparisons above
    /// both pass on it, since the shape is neither a missing key nor a missing hole.</para>
    ///
    /// <para>No allowlist. A string could in principle need a literal one — a file mask, a quoted
    /// command — and none does; inventing the escape hatch for a case that has never occurred is how a
    /// check becomes something people route around. Add one the day a string needs it, and let the red
    /// build be that conversation.</para>
    /// </summary>
    private static void NoParenthesisedPlural()
    {
        var shape = new Regex(@"\p{L}\(\p{Ll}{1,3}\)", RegexOptions.Compiled);
        string[] found = L.Codes
            .SelectMany(code => L.Strings(code).Where(kv => shape.IsMatch(kv.Value))
                                              .Select(kv => $"{code}:{kv.Key}"))
            .Order(StringComparer.Ordinal).ToArray();
        Check($"no string counts with a parenthesised plural ({L.Codes.Count} languages)",
              found.Length == 0,
              Named(found, "written as word(s) — the pair is `<stem>.one` / `<stem>.many` through L.N"));
    }

    /// <summary>
    /// The counted strings, held as pairs (T376). <c>L.N</c> asks for <c>&lt;stem&gt;.one</c> or
    /// <c>&lt;stem&gt;.many</c> by the number, and a stem carrying only one of the two prints the missing
    /// half in English on whichever count the translator did not think of — or the raw key, if English is
    /// the file that is short.
    ///
    /// <para><b>The plural form has to take the count; the singular does not.</b> That asymmetry is not a
    /// concession, it is the data: the three pairs that predate <c>L.N</c> write their singulars as
    /// <c>"1 minute"</c> and <c>", 1 week away excluded"</c>, and they are right to — in the singular the
    /// number is one, so spelling it is prose rather than a hole, and several languages drop it entirely.
    /// A <c>.many</c> with no <c>{0}</c> is the real defect: it says "folders" and never how many.</para>
    ///
    /// <para>Swept over <c>en</c> alone, because the parity checks above already hold every other file to
    /// it key for key: a stem complete here and half-translated there is <c>missing</c>, not this.</para>
    /// </summary>
    private static void Plurals(IReadOnlyDictionary<string, string> en)
    {
        string[] stems = en.Keys
            .Where(k => L.Plural.Any(p => k.EndsWith(p, StringComparison.Ordinal)))
            .Select(k => k[..k.LastIndexOf('.')])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        // The precondition: a rename that lost the suffix convention would leave every claim below holding
        // over an empty set, which is the one way this check can go quiet.
        if (!Check($"the counted strings are found by their suffixes ({stems.Length})", stems.Length > 0,
                   $"no key ends in {string.Join(" or ", L.Plural)}, so nothing was compared"))
            return;

        string[] halved = stems.Where(s => !L.Plural.All(p => en.ContainsKey(s + p)))
                               .Order(StringComparer.Ordinal).ToArray();
        Check($"every counted string is written in both forms ({stems.Length})", halved.Length == 0,
              Named(halved, $"carrying only one of {string.Join(" / ", L.Plural)}, so the other count "
                            + "falls back or prints the key"));

        string[] countless = stems.Select(s => s + L.Plural[1])
                                  .Where(k => en.TryGetValue(k, out string? v)
                                              && !v.Contains("{0}", StringComparison.Ordinal))
                                  .Order(StringComparer.Ordinal).ToArray();
        Check($"and every plural form takes the count ({stems.Length})", countless.Length == 0,
              Named(countless, "having no {0}, so it says what was counted and never how many"));
    }

    // The keys of one table matching a predicate, ordered so the same gap reads the same way twice.
    private static string[] Keys(IReadOnlyDictionary<string, string> table, Func<string, bool> bad) =>
        table.Keys.Where(bad).Order(StringComparer.Ordinal).ToArray();

    /// <summary>The <c>{0}</c>-style holes a string carries, deduplicated and ordered, as one comparable
    /// text. <c>{{</c> is <see cref="string.Format(string, object[])"/>'s escape for a literal brace, so it
    /// is stepped over rather than read as the start of a hole.</summary>
    private static string Holes(string s)
    {
        var found = new SortedSet<int>();
        for (int i = 0; i < s.Length - 1; i++)
        {
            if (s[i] != '{') continue;
            if (s[i + 1] == '{') { i++; continue; }
            int j = i + 1, n = 0;
            while (j < s.Length && char.IsAsciiDigit(s[j])) n = n * 10 + (s[j++] - '0');
            if (j > i + 1) found.Add(n);
        }
        return string.Join(",", found);
    }

    /// <summary>
    /// T314. The direction <see cref="Translations"/> cannot look: every key the <em>sources</em> name is a
    /// key <c>en.json</c> holds.
    ///
    /// <para><see cref="L.T(string)"/> ends in <c>TryGetValue(key, out var en) ? en : key</c>, so a
    /// misspelled key is returned verbatim and <c>stats.legend.overQuota.tipBand</c> renders where a
    /// sentence belongs. Nothing saw it: the parity check loads <c>en</c> and compares the other four to
    /// <em>it</em>, so <c>en</c> is the source of truth for what exists and every failure it can find is a
    /// translation gap. This one is an English gap, and English is the fallback, so there is nothing behind
    /// it. T313's checks came within one character — they assert which key each legend state
    /// <em>chooses</em>, comparing a C# literal to a C# literal, which passes whether or not a table holds
    /// it.</para>
    ///
    /// <para><b>Where this stops</b>, said here rather than in a later surprise. A key built at run time
    /// cannot be seen — <c>ApplyOverLegend</c> passes <c>g.TipKey</c>, and the method note passes
    /// <c>f.Key</c> — so this covers the literals and says how many it read. And it is a subset check in one
    /// direction only: a key in <c>en</c> that no call site names is dead weight, not a defect on screen,
    /// and pruning it is a different task.</para>
    /// </summary>
    private static void SpelledKeys() =>
        Repo("every localization key the sources name is one en.json holds", SpelledKeys, "src");

    /// <summary>The <c>{local:Loc key}</c> uses in markup. Every one in this repository is that bare form —
    /// the <c>Key=</c> spelling the extension also accepts appears nowhere, so it is not matched here: a
    /// branch no source exercises is a branch nothing checks.</summary>
    private static readonly Regex LocMarkup = new(@"local:Loc\s+([^}\s,""]+)", RegexOptions.Compiled);

    private static void SpelledKeys(string root)
    {
        IReadOnlyDictionary<string, string> en = L.Strings("en");
        if (!Check($"the base table loads ({en.Count} keys)", en.Count > 0,
                   "en.json parsed to no keys at all, so every key below would be reported missing"))
            return;

        // Ordered, and carrying where each was named: the same gap reads the same way twice, and a report
        // that names the file is one somebody can act on without a second search.
        var named = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        void Name(string key, string file) =>
            (named.TryGetValue(key, out SortedSet<string>? at) ? at : named[key] = new(StringComparer.Ordinal))
                .Add(file);

        string src = Path.Combine(root, "src");
        int fromCode = 0, fromMarkup = 0, calls = 0, unseen = 0;
        foreach (string path in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
            foreach (string[] keys in KeysNamed(CodeOf(File.ReadAllText(path))))
            {
                calls++;
                if (keys.Length == 0) unseen++;
                foreach (string key in keys) { Name(key, Path.GetFileName(path)); fromCode++; }
            }
        foreach (string path in Directory.GetFiles(src, "*.xaml", SearchOption.AllDirectories))
            foreach (Match m in LocMarkup.Matches(File.ReadAllText(path)))
            {
                Name(m.Groups[1].Value, Path.GetFileName(path));
                fromMarkup++;
            }

        // Both halves, separately: one of them going to zero while the other still finds plenty is a scan
        // that has stopped reading, and a subset check over nothing passes. The calls this cannot see are
        // counted in the same breath, so the limit is a number in the summary rather than a later surprise.
        if (!Check($"the scan reads both surfaces ({fromCode} keys named in code, {fromMarkup} in markup, " +
                   $"{named.Count} distinct — and {unseen} of {calls} calls name no literal, so the key they " +
                   "build at run time is invisible here)", fromCode > 0 && fromMarkup > 0,
                   "a surface yielding no key at all is a scan that has stopped reading, not a repository " +
                   "that has stopped naming keys"))
            return;

        string[] missing = named.Keys.Where(k => !en.ContainsKey(k))
                                .Select(k => $"{k} ({string.Join(", ", named[k])})").ToArray();
        Check($"every key the sources name is one en.json holds ({named.Count})", missing.Length == 0,
              Named(missing, "named by the code and held by no table, so L.T returns the key itself and a " +
                             "dotted identifier lands where a sentence belongs"));
    }

    private static readonly Regex LocCall = new(@"\bL\.T\(", RegexOptions.Compiled);

    /// <summary>
    /// <see cref="KeysNamed"/>'s own answer, over the argument forms this repository writes — asserted
    /// against synthetic source rather than against the tree (T248's reason: the tree happens not to hold
    /// every case today, and a check that waits for one ships broken).
    ///
    /// <para>Two directions, and the second is the quiet one. Reading <em>more</em> than is there — a
    /// format argument taken for a key — fails loudly, in the check above, naming a string nobody meant as
    /// a key. Reading <em>less</em> is silent: a branch of a ternary skipped is a key nothing holds up, and
    /// the subset check still passes. So the fixtures assert the exact set, and each awkward literal is
    /// followed by a string that must <em>not</em> be collected — a terminator read wrong runs the walk
    /// past the closing paren and would take it.</para>
    /// </summary>
    private static void KeyScanning()
    {
        // Assembled, never written out. This file is under src\, so a fixture containing a real L.T("…")
        // would be, to the scan that reads that folder, a key the tables have forgotten — and it would be
        // right. Building the token means the fixture cannot be mistaken for the thing it is a fixture of.
        const string Q = "\"";
        static string Key(string k) => Q + k + Q;
        static string Call(string arg) => "L" + ".T(" + arg + ")";

        // The tail every case carries: a literal after the call, which only a walk that ended where the
        // call ended leaves alone.
        string tail = "; string s = " + Key("not.a.key") + ";";

        (string What, string Arg, string[] Keys)[] cases =
        {
            ("a plain literal", Key("a.plain"), new[] { "a.plain" }),
            ("both branches of a ternary", $"flag ? {Key("a.left")} : {Key("a.right")}",
                new[] { "a.left", "a.right" }),
            ("every arm of a switch expression",
                $"kind switch {{ Kind.One => {Key("a.one")}, _ => {Key("a.other")} }}",
                new[] { "a.one", "a.other" }),
            ("a key beside a format argument", $"{Key("a.format")}, {Key("N/A")}", new[] { "a.format" }),
            ("a key built at run time", "f.Key", Array.Empty<string>()),
            ("an interpolated key", "$" + Q + "a.{n}" + Q, Array.Empty<string>()),
            // Each of these ends in a way QuotedEnd alone reads wrong, which is how the walk would run on.
            ("a verbatim literal ending in a backslash", "@" + Q + @"c:\" + Q, Array.Empty<string>()),
            ("a raw literal carrying quotes", Q + Q + Q + "he said " + Q + "hi" + Q + " " + Q + Q + Q,
                Array.Empty<string>()),
            ("a char literal holding a quote", $"c == '{Q}' ? {Key("a.quote")} : {Key("a.other")}",
                new[] { "a.quote", "a.other" }),
        };

        // Quoted, because a walk that ran off the end of a literal collects the empty string — and an
        // unquoted report of that reads as the empty list, which is what a correct run looks like.
        static string Show(IEnumerable<string> keys) =>
            "[" + string.Join(", ", keys.Select(k => $"'{k}'")) + "]";

        var wrong = new List<string>();
        foreach ((string what, string arg, string[] expected) in cases)
        {
            string[][] read = KeysNamed(CodeOf(Call(arg) + tail)).ToArray();
            if (read.Length != 1 || !read[0].SequenceEqual(expected))
                wrong.Add($"{what} → {Show(read.SelectMany(r => r))} where {Show(expected)} was named");
        }
        Check($"every argument form is read as the keys it names ({cases.Length})", wrong.Count == 0,
              string.Join("; ", wrong));

        // A comment is not code, and the scan's input is CodeOf's output — the property that keeps this
        // file's own prose about L.T out of the key set.
        string[][] commented = KeysNamed(CodeOf("// " + Call(Key("a.commented")))).ToArray();
        Check("a key quoted in a comment is named by nobody", commented.Length == 0,
              Show(commented.SelectMany(r => r)));
    }

    // ---------------------------------------------------------------- Block AI: the tooltip's budget

    /// <summary>
    /// The 127 characters of <c>NOTIFYICONDATA.szTip</c>, and what the tooltip does with them (T215).
    ///
    /// <para>This budget rations the most-seen text this app produces, and until T214 moved the
    /// composition off <see cref="TrayContext"/> nothing could reach it: a tray cannot be constructed
    /// headlessly, so the rule was held up by whoever next hovered an icon. T213 spent about eight of
    /// these characters in five languages without being able to check what it spent.</para>
    ///
    /// <para><b>What measuring it found.</b> French with an overage line composed 129 characters — over
    /// the cap, so Windows truncated the end, which is the status line carrying the time of the reading.
    /// Nobody had measured it because nobody could. The composition now sheds the bounded window the icon
    /// is <em>not</em> about before that can happen, and these assertions are what keeps it true in a
    /// language nobody here reads.</para>
    /// </summary>
    private static void Tooltip()
    {
        // The app's own list, not a copy of it: a sixth language is covered here with no edit.
        string[] codes = L.Codes.ToArray();
        IReadOnlyList<TooltipCli.Variant> variants = TooltipCli.Catalogue;
        const long Now = 1_800_000_000;

        if (!Check("the tooltip catalogue has its states", variants.Count >= 8, $"{variants.Count}"))
            return;

        // Restored however this section exits: every later section reads L, and leaving the process in
        // Spanish would rewrite what they assert against.
        L.Lang saved = L.Current;
        try
        {
            var over = new List<string>();
            var noStatus = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                foreach (TooltipCli.Variant v in variants)
                {
                    string text = TooltipText.Compose(v.Build(Now));
                    if (text.Length > TooltipText.Cap) over.Add($"{code}/{v.Name} ({text.Length})");
                    // The last line is the status line, and it is the one a blind end-truncation would
                    // cut mid-value. Whole means whole: it is never the thing that gets shortened.
                    string last = text.Split('\n')[^1];
                    if (!text.EndsWith(last, StringComparison.Ordinal) || last.Length == 0)
                        noStatus.Add($"{code}/{v.Name}");
                }
            }
            Check($"no composed tooltip exceeds {TooltipText.Cap} chars, in any of the five languages " +
                  $"({codes.Length * variants.Count} combinations)", over.Count == 0,
                  $"{string.Join(", ", over)} — Windows truncates the end, which is the reading's time");
            Check("and every one of them ends on a whole status line", noStatus.Count == 0,
                  string.Join(", ", noStatus));

            L.Apply("fr");   // the longest of the five, so the budget actually bites

            // The label is what the user typed: the one input with no bound of its own, and an unbounded
            // line makes the cap a claim nothing can keep.
            string huge = new('W', 400);
            string withHuge = TooltipText.Compose(Long(Now) with { ProfileLabel = huge });
            Check("a profile label of any length cannot overrun the cap",
                  withHuge.Length <= TooltipText.Cap, $"{withHuge.Length} chars from a {huge.Length}-char label");

            // §XX.13's own asks, in order. The label is kept when the projection cannot be: knowing whose
            // quota this is outranks a wordier sentence, and that trade is the reason the budget is
            // spent in this order rather than the other.
            string tight = TooltipText.Compose(Long(Now) with { ProfileLabel = "Trabalho" });
            Check("the profile label survives when the projection cannot",
                  tight.Contains("Trabalho", StringComparison.Ordinal),
                  "the line naming whose quota this is was dropped to make room for a sentence");

            // The compact form exists to be taken. A run where it would have fitted and the line was
            // dropped anyway is the budget failing at the one job it has.
            var dropped = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                string composed = TooltipText.Compose(Long(Now) with { ProfileLabel = "Trabalho" });
                int room = TooltipText.Cap - composed.Length;
                string compact = L.T("tip.okEtaCompact", "100%", "1d 2h");
                if (!composed.Contains(compact, StringComparison.Ordinal) && room >= compact.Length + 1)
                    dropped.Add($"{code} (room {room}, compact {compact.Length})");
            }
            Check("the compact projection is taken whenever it fits, never dropped", dropped.Count == 0,
                  string.Join(", ", dropped));

            // What goes first when the readings alone are over budget: the bounded window the icon is not
            // reporting. The watched one is the whole reason the tooltip is being read.
            L.Apply("fr");
            string overflowing = TooltipText.Compose(Overflowing(Now));
            Check("an over-budget reading sheds the window the icon is not about",
                  overflowing.Contains(L.T("tip.week"), StringComparison.Ordinal) &&
                  !overflowing.Contains(L.T("tip.session"), StringComparison.Ordinal),
                  $"kept the wrong line: {overflowing.Replace("\n", " | ")}");

            // T222. The state where extra usage is paying is the only state whose news was never on
            // screen: its overage reading exists *only* there and cost about what its one sentence needed,
            // so in all five languages `atlimit` rendered a sentence and this rendered none — the two
            // opposite pieces of news T182 split apart looked identical. The property is not that a
            // particular line exists; it is that the news arrives, in every language, whatever the budget.
            var mute = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                string billing = TooltipText.Compose(Overflowing(Now));
                if (!billing.Contains(L.T("tip.extraPaying", "47%"), StringComparison.Ordinal))
                    mute.Add($"{code} ({billing.Length}/{TooltipText.Cap})");
            }
            Check("the state where extra usage is paying says so, in every language", mute.Count == 0,
                  $"{string.Join(", ", mute)} — the one state whose news never reached the screen");

            // T288, the same property for the opposite outcome. The account is refused and the icon is on
            // the window that is fine, which is exactly where the sentence used to read "on track". Asked of
            // every language because the first draft of this fix rendered nothing at all in es and fr: the
            // natural sentence cost more than those readings had left, and a dropped line and a wrong line
            // are equally silent about being blocked.
            var quiet = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                string stopped = TooltipText.Compose(StoppedElsewhere(Now));
                if (!stopped.Contains(L.T("tip.atLimitUnscoped"), StringComparison.Ordinal) &&
                    !stopped.Contains(L.T("tip.atLimitUnscopedCompact"), StringComparison.Ordinal))
                    quiet.Add($"{code} ({stopped.Length}/{TooltipText.Cap})");
            }
            Check("a blocked account says so even when the icon is on the window with room", quiet.Count == 0,
                  $"{string.Join(", ", quiet)} — neither rung fitted, so the reading is mute about being blocked");

            // And it says it *unscoped*. The whole defect is a caption about the wrong window, so a fix that
            // captions the wrong window with a different word has not fixed anything: at 47% neither "Week
            // 7d: at limit" nor the on-track projection may appear.
            L.Apply("en");
            string blocked = TooltipText.Compose(StoppedElsewhere(Now));
            Check("and names neither the window with room nor its pace",
                  !blocked.Contains(L.T("tip.atLimitFull", TrayContext.MetricLabel("7d"), L.T("tip.limitUsed")),
                                    StringComparison.Ordinal) &&
                  !blocked.Contains(L.T("tip.okTrackFull", TrayContext.MetricLabel("7d")), StringComparison.Ordinal) &&
                  !blocked.Contains(L.T("tip.okTrackCompact"), StringComparison.Ordinal),
                  blocked.Replace("\n", " | "));

            // The other side of the same rule, which is T274's and is what stops this becoming a blanket
            // unscoping: where the metric IS the window that crossed, the figure and the caption agree and
            // the scoped sentence is the better one. Nothing here should have taken that away.
            string onIt = TooltipText.Compose(StoppedElsewhere(Now) with { Metric = "5h" });
            Check("while the window that actually crossed keeps its scoped sentence",
                  onIt.Contains(L.T("tip.atLimitFull", TrayContext.MetricLabel("5h"), L.T("tip.limitUsed")),
                                StringComparison.Ordinal),
                  onIt.Replace("\n", " | "));

            // And it must arrive *once*: the merge exists because the reading and the sentence were the
            // same fact twice. At the real cap this cannot be tested — there is no room for both, so a
            // composition emitting both would still render one, and the assertion would pass on a broken
            // rule. Given room, what keeps it single has to be the rule itself, so the room is asserted
            // first rather than assumed.
            L.Apply("en");
            string roomy = TooltipText.Compose(Roomy(Now));
            string sentence = L.T("tip.billingCompact");
            // Measured over the readings rather than over the output: a composition that wrongly emitted
            // the sentence has already spent the room, and judging it by what is left would fail the
            // precondition instead of the property — the guard reporting that it could not look, when what
            // it is looking at is exactly the defect.
            int spare = TooltipText.Cap - roomy.Length
                        + (roomy.Contains(sentence, StringComparison.Ordinal) ? sentence.Length + 1 : 0);
            if (Check($"a billing reading with room to spare has room for a second sentence",
                      spare >= sentence.Length + 1, $"{spare} spare < {sentence.Length + 1} needed"))
                Check("and the news is still said once, on the reading it is about",
                      !roomy.Contains(sentence, StringComparison.Ordinal)
                      && roomy.Contains(L.T("tip.extraPaying", "47%"), StringComparison.Ordinal),
                      roomy.Replace("\n", " | "));

            // The other half of the same state, and the reason the sentence is kept rather than deleted:
            // extra usage enabled with nothing spent yet is billing with no overage reading to merge into.
            // Nothing else in the tooltip would say so, and here the sentence is affordable.
            var silent = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                string unspent = TooltipText.Compose(Unspent(Now));
                if (!unspent.Contains(L.T("tip.billingCompact"), StringComparison.Ordinal)
                    && !unspent.Contains(L.T("tip.billingFull", L.T("tip.week")), StringComparison.Ordinal))
                    silent.Add(code);
            }
            Check("billing with nothing spent yet still has a sentence to say it with", silent.Count == 0,
                  $"{string.Join(", ", silent)} — merged away the line that had nothing to merge into");

            // T274. The verdict is about the account and the caption is about the window on the icon, so
            // the one case where they disagree is the one that has to be read: a rejected session behind a
            // week at 47%. The news must arrive in every language — that is T222's property, and the whole
            // point of rescoping the state is that it now arrives here too — and it must arrive *without*
            // the week's name on it, because "Week 7d: past your included quota" captions 47%.
            var unscoped = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                string elsewhere = TooltipText.Compose(Elsewhere(Now));
                if (!elsewhere.Contains(L.T("tip.billingCompact"), StringComparison.Ordinal)
                    || elsewhere.Contains(L.T("tip.billingFull", L.T("tip.week")), StringComparison.Ordinal))
                    unscoped.Add($"{code}: {elsewhere.Replace("\n", " | ")}");
            }
            Check("billing on a window the icon is not showing says so, and captions no percentage",
                  unscoped.Count == 0, string.Join("  //  ", unscoped));

            L.Apply("en");
            // And the scoped form is not gone — it is what the window that actually crossed still gets.
            // Asked with the budget slack for the reason the merge is: at the real cap the full form does
            // not fit either way, so a rule that had stopped choosing it would pass on the fallback.
            string crossedHere = TooltipText.Compose(Roomy(Now) with
            {
                Data = new UsageData { Session5h = 0.40, Week7d = 1.0, Status = "allowed", Status7d = "allowed" },
            });
            Check("the window that did cross keeps the scoped sentence, given room for it",
                  crossedHere.Contains(L.T("tip.billingFull", L.T("tip.week")), StringComparison.Ordinal),
                  crossedHere.Replace("\n", " | "));

            // T280. A spell is an event, and "since when" is the half no line carried. Deliberately NOT
            // T222's property: that one is "the news arrives whatever the budget", and this line is not news
            // — it qualifies news the sentence above it carries, so it is the last rung of the ladder and the
            // first thing dropped. What can be asserted is that the ladder is honest about it: a form that
            // would have fitted is never dropped anyway, which is the same rule the compact projection keeps
            // above, and the one a translation spending four more characters breaks silently.
            var wasted = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                foreach (TooltipCli.Variant v in variants)
                {
                    TooltipText.Input built = v.Build(Now);
                    if (built.SpellSince <= 0) continue;
                    string dur = TrayContext.FmtDays(built.Now - built.SpellSince);
                    string[] rungs =
                    {
                        L.T("tip.spellFull", dur), L.T("tip.spellCompact", dur), L.T("tip.spellBare", dur),
                    };
                    string text = TooltipText.Compose(built);
                    if (rungs.Any(r => text.Contains(r, StringComparison.Ordinal))) continue;
                    // Nothing was emitted, so every character of the room is still there to measure — and the
                    // cheapest rung is the one the room has to be judged against (T302).
                    int room = TooltipText.Cap - text.Length;
                    if (room >= rungs[^1].Length + 1)
                        wasted.Add($"{code}/{v.Name} (room {room}, cheapest {rungs[^1].Length})");
                }
            }
            Check("the spell's duration is taken whenever it fits, never dropped", wasted.Count == 0,
                  string.Join(", ", wasted));

            // T302 got this to "somewhere in every language"; T306 makes it every state, by letting the one
            // reading the user's own display setting did NOT choose be given up rather than say nothing.
            // Counted rather than named: which language runs out of room is a fact about translations, and a
            // list of the ones that do is a list that goes stale on the next rewording.
            var unsaid = new List<string>();
            // The same readings with no spell to state, so the two compositions differ only by this line —
            // which is how "did a reading get given up for it" can be asked at all.
            int Readings(string text)
            {
                string s5 = L.T("tip.session"), w7 = L.T("tip.week");
                return text.Split('\n').Count(l => l.Contains('%')
                    && (l.StartsWith(s5, StringComparison.Ordinal) || l.StartsWith(w7, StringComparison.Ordinal)));
            }

            var paidForAGlyph = new List<string>();
            var shedWithoutNeed = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                foreach (TooltipCli.Variant v in variants)
                {
                    TooltipText.Input built = v.Build(Now);
                    if (built.SpellSince <= 0) continue;
                    string dur = TrayContext.FmtDays(built.Now - built.SpellSince);
                    string full = L.T("tip.spellFull", dur), compact = L.T("tip.spellCompact", dur);
                    string bare = L.T("tip.spellBare", dur);

                    string with = TooltipText.Compose(built);
                    string without = TooltipText.Compose(built with { SpellSince = 0 });
                    bool worded = with.Contains(full, StringComparison.Ordinal)
                                  || with.Contains(compact, StringComparison.Ordinal);
                    if (!worded && !with.Contains(bare, StringComparison.Ordinal))
                    {
                        unsaid.Add($"{code}/{v.Name}");
                        continue;
                    }

                    if (Readings(with) >= Readings(without)) continue;
                    // A reading was given up for this line, and both halves of T306's rule bind here. It must
                    // buy words — and it may only happen where the alternative was silence, which `without`
                    // answers: it is this same composition carrying both readings and no spell line, so the
                    // room it leaves is exactly the room a rung would have had if nothing were shed.
                    if (!worded) paidForAGlyph.Add($"{code}/{v.Name}");
                    if (TooltipText.Cap - without.Length >= bare.Length + 1)
                        shedWithoutNeed.Add($"{code}/{v.Name} (room was {TooltipText.Cap - without.Length})");
                }
            }
            Check($"every billing state says how long, in every language " +
                  $"({codes.Length * variants.Count(v => v.Build(Now).SpellSince > 0)} combinations)",
                  unsaid.Count == 0,
                  $"silent: {string.Join(", ", unsaid)} — a state that costs money, with the duration behind " +
                  "it unsayable");
            Check("a reading is never given up for a wordless duration", paidForAGlyph.Count == 0,
                  $"{string.Join(", ", paidForAGlyph)} — a measured percentage traded for a glyph");
            Check("nor given up where the duration would have fitted anyway", shedWithoutNeed.Count == 0,
                  $"{string.Join(", ", shedWithoutNeed)} — §XXXIII's rule stands except against silence, so a " +
                  "state with room for a rung keeps both windows");

            // The cheapest rung is a glyph and a duration, and the glyph carries the whole meaning. It must
            // not be the one that already means "resets in": the line above ends in `⟳ 3d 0h`, so reusing it
            // would put two identical markers on one card pointing in opposite directions in time.
            var confusable = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                if (L.T("tip.spellBare", "3h 20m").Contains('⟳')) confusable.Add(code);
            }
            Check("and the marker it leads with is not the one that means 'resets in'",
                  confusable.Count == 0,
                  $"{string.Join(", ", confusable)} — elapsed and remaining cannot wear one glyph");

            // And where it is taken it is taken in full whenever the full form fits, so the compact rung is a
            // fallback rather than what every language silently ends up with.
            var shortchanged = new List<string>();
            foreach (string code in codes)
            {
                L.Apply(code);
                string dated = TooltipText.Compose(Roomy(Now) with { SpellSince = Now - 3 * 3600 - 20 * 60 });
                string full = L.T("tip.spellFull", "3h 20m"), compact = L.T("tip.spellCompact", "3h 20m");
                if (!dated.Contains(full, StringComparison.Ordinal)
                    && dated.Contains(compact, StringComparison.Ordinal)
                    && TooltipText.Cap - dated.Length + compact.Length >= full.Length)
                    shortchanged.Add($"{code} ({dated.Length}/{TooltipText.Cap})");
            }
            Check("and in full whenever the full form fits", shortchanged.Count == 0,
                  string.Join(", ", shortchanged));

            // And the duration ages with the clock rather than freezing at the poll that measured it: the
            // input is the moment, not the elapsed time, which is the whole reason it is stored that way.
            L.Apply("en");
            string later = TooltipText.Compose(Roomy(Now) with
            {
                SpellSince = Now - 3 * 3600 - 20 * 60,
                Now = Now + 3600,
            });
            Check("and the same crossing an hour later reads an hour longer",
                  later.Contains(L.T("tip.spellCompact", "4h 20m"), StringComparison.Ordinal)
                  || later.Contains(L.T("tip.spellFull", "4h 20m"), StringComparison.Ordinal),
                  later.Replace("\n", " | "));

            // The line is about a spell, so a reading not in one may not carry it whatever the budget allows.
            // Both halves matter: an account inside its quota, and a billing account whose crossing is not on
            // file — the field-report shape, where the honest answer is to say nothing.
            string inQuota = TooltipText.Compose(Roomy(Now) with
            {
                State = QuotaState.InQuota,
                SpellSince = Now - 3 * 3600,
            });
            string nothingOnFile = TooltipText.Compose(Roomy(Now));
            Check("a reading with no spell to date carries no duration",
                  !inQuota.Contains(L.T("tip.spellCompact", "3h 0m"), StringComparison.Ordinal)
                  && !nothingOnFile.Contains(L.T("tip.spellCompact", "3h 20m"), StringComparison.Ordinal)
                  && !nothingOnFile.Contains(L.T("tip.spellFull", "3h 20m"), StringComparison.Ordinal),
                  $"{inQuota.Replace("\n", " | ")}  //  {nothingOnFile.Replace("\n", " | ")}");

            // And it is the rung that goes first. The billing sentence is the news; the duration qualifies
            // it, so a tooltip that could afford only one of them must keep the sentence.
            L.Apply("fr");
            string tightSpell = TooltipText.Compose(Elsewhere(Now) with { SpellSince = Now - 26 * 3600 });
            Check("at the cap the duration is dropped before the sentence it qualifies",
                  tightSpell.Length <= TooltipText.Cap
                  && tightSpell.Contains(L.T("tip.billingCompact"), StringComparison.Ordinal),
                  $"{tightSpell.Length}/{TooltipText.Cap}: {tightSpell.Replace("\n", " | ")}");
        }
        finally { L.Apply(L.Codes[(int)saved]); }
    }

    // ---------------------------------------------------------------- Block A: the handover between accounts

    /// <summary>
    /// T263. A date's <b>order</b> comes from the culture, not from a pattern written in the source. It did
    /// not: <c>"MMM d"</c> rendered <em>"août 3"</em> in French — where all four non-English cultures put
    /// the day first — and <c>"d/M"</c> rendered <c>3/8</c> to an American, whose short date is <c>M/d</c>,
    /// on the very chart the published screenshots are of.
    ///
    /// <para><b>Asserted against each culture's own patterns</b>, over every language this build ships, so
    /// a language added later is covered by having been added. The claim is the one §XXX.1 named: whichever
    /// of day and month comes first in what the page renders is the one that comes first in that culture's
    /// <c>MonthDayPattern</c> — read off the rendered string by where the two numbers land, which is what
    /// makes this a check about output rather than about the pattern the code happens to hold.</para>
    ///
    /// <para>T167's number sweep cannot reach this and was never meant to: it varies
    /// <c>CurrentCulture</c> and demands the answer not move, while a date is the one thing that must.</para>
    /// </summary>
    private static void DateOrder()
    {
        System.Globalization.CultureInfo before = System.Globalization.CultureInfo.CurrentCulture;
        string beforeLang = L.Codes.FirstOrDefault(c => L.Resolve(c) == L.Current) ?? "en";
        try
        {
            // A day and a month that cannot be confused for each other, or "3/8" and "8/3" read the same.
            var when = new DateTime(2026, 8, 23, 16, 26, 0);
            foreach (string code in L.Codes)
            {
                L.Apply(code);
                var culture = L.DateCulture;
                bool monthFirstInCulture =
                    culture.DateTimeFormat.MonthDayPattern.IndexOf('M', StringComparison.Ordinal)
                    < culture.DateTimeFormat.MonthDayPattern.IndexOf('d', StringComparison.Ordinal);

                string named = Dates.MonthDay(when);
                bool monthFirstAsRendered = named.IndexOf("23", StringComparison.Ordinal) > 0;
                Check($"{code}: the named month sits where this culture puts it ('{named}')",
                      monthFirstAsRendered == monthFirstInCulture,
                      $"culture wants month-first={monthFirstInCulture}, rendered '{named}'");

                string digits = Dates.DayMonthDigits(when);
                bool monthFirstInShort =
                    culture.DateTimeFormat.ShortDatePattern.IndexOf('M', StringComparison.Ordinal)
                    < culture.DateTimeFormat.ShortDatePattern.IndexOf('d', StringComparison.Ordinal);
                Check($"{code}: and so do the digits ('{digits}')",
                      digits.StartsWith(monthFirstInShort ? "8" : "23", StringComparison.Ordinal),
                      $"short date is '{culture.DateTimeFormat.ShortDatePattern}', rendered '{digits}'");

                Check($"{code}: both fields are actually there",
                      named.Contains("23", StringComparison.Ordinal)
                      && digits.Contains("23", StringComparison.Ordinal)
                      && digits.Contains('8', StringComparison.Ordinal), $"'{named}' / '{digits}'");
            }

            // The precondition: if every shipped culture agreed on the order, the sweep above would pass
            // over a hardcoded pattern too and prove nothing.
            string[] orders = L.Codes.Select(c =>
            {
                L.Apply(c);
                return L.DateCulture.DateTimeFormat.MonthDayPattern.IndexOf('M', StringComparison.Ordinal)
                       < L.DateCulture.DateTimeFormat.MonthDayPattern.IndexOf('d', StringComparison.Ordinal)
                           ? "month" : "day";
            }).Distinct(StringComparer.Ordinal).ToArray();
            Check("the shipped languages disagree about the order — so the sweep above is not vacuous",
                  orders.Length > 1, $"all of them put the {orders.FirstOrDefault()} first");
        }
        finally
        {
            L.Apply(beforeLang);
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    /// <summary>
    /// T260. <c>--lang</c> is the flag the i18n verification loop is built on, and it used to honour a
    /// code this build does not ship by quietly using the machine's own language instead: <c>--lang zz
    /// --tooltip</c> printed a tooltip in <c>PtBr</c> and exited 0. So a typo in
    /// <c>--lang fr --capture-toast extra out.png</c> wrote the card in Portuguese into the file the
    /// caller named, and nothing said so — a screenshot of the wrong thing looking exactly like a
    /// screenshot of the right one, which is what T186, T198, T200 and T231 each refuse.
    ///
    /// <para>Swept over <see cref="L.Codes"/> rather than a list written here, so a language added
    /// tomorrow is covered tomorrow — and asserted in both directions, because a refusal that refuses
    /// everything would satisfy half of this and break the flag.</para>
    /// </summary>
    private static void LangOverride()
    {
        string[] refusedShipped = L.Codes.Where(c => L.RefuseOverride(c) is not null).ToArray();
        Check($"every language this build ships is accepted ({L.Codes.Count})", refusedShipped.Length == 0,
              $"{string.Join(", ", refusedShipped)} — refused by the flag that exists to select them");

        Check("and so is auto, which asks for the OS language out loud",
              L.RefuseOverride("auto") is null);

        // The other direction. A near-miss and a wrong case are the typos that actually happen, and the
        // codes are matched exactly (`L.Resolve` is ordinal), so both must be refused rather than resolved.
        foreach (string bad in new[] { "zz", "de", "pt_BR", "EN", "en-GB", "" })
            Check($"'{bad}' is refused rather than becoming the machine's language",
                  L.RefuseOverride(bad) is not null);

        Check("the flag with no code after it is refused too — it was typed, and cannot be honoured",
              L.RefuseOverride(null) is not null);

        // A refusal nobody can act on is the reason these flags print a catalogue (T186).
        string refusal = L.RefuseOverride("zz") ?? "";
        string[] unlisted = L.Codes.Where(c => !refusal.Contains(c, StringComparison.Ordinal)).ToArray();
        Check($"and the refusal prints every code it would have accepted ({L.Codes.Count})",
              unlisted.Length == 0 && refusal.Contains("auto", StringComparison.Ordinal),
              $"missing from the catalogue: {string.Join(", ", unlisted)}");
    }

    /// <summary>
    /// T167: the Statistics page states its numbers in one convention, whatever the machine's locale is.
    /// Thirteen formatters ended in <c>Fmt</c> and the method note's five interpolations did not, so on a
    /// pt-BR machine the English popup read <em>"4,7 weeks of local transcripts"</em> eight lines above
    /// <c>≈ 1,319 tok/s</c> and <c>40%</c> — in both verification screenshots of T159 and T163, unseen.
    ///
    /// <para><b>T216 pointed it at the rest of the app.</b> The sweep reached one window while four other
    /// surfaces stated the same kinds of figure, so whichever convention the app had chosen was held up on
    /// one page and nowhere else. <see cref="Nums"/> now carries the rule — numbers invariant, dates in the
    /// display language — and <see cref="Surfaces"/> lists the types that put a number in front of a reader:
    /// every static formatter on each is swept, so a <em>type</em> is what has to be remembered, not a
    /// method. Adding a formatter to a surface already here costs nothing.</para>
    ///
    /// <para>The formatters are <b>derived, never listed</b>, for the same reason the automation-id sweep
    /// derives its pages: a hardcoded list stops covering whatever is written next, which is the defect
    /// the note itself was. Every static method on those types that turns numbers into a string is run
    /// twice, once under each culture, and any that answers differently has read the OS.</para>
    ///
    /// <para>What this cannot reach is an interpolation written <em>inline</em> in a render method rather
    /// than in a formatter — the note's own shape until T167, and the shape every surface T216 converted
    /// was in. Pulling one out into a static formatter is what puts it inside this sweep; leaving it inline
    /// is what keeps it out, which is the cost §XV.2 (T168) is about.</para>
    /// </summary>
    private static void Formatting()
    {
        System.Globalization.CultureInfo before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // Cloned from invariant so *only* the number conventions differ: a real locale would also
            // change month names, which `DateFmt` owns deliberately and this sweep is not about.
            var hostile = (System.Globalization.CultureInfo)
                System.Globalization.CultureInfo.InvariantCulture.Clone();
            hostile.NumberFormat.NumberDecimalSeparator = ",";
            hostile.NumberFormat.NumberGroupSeparator = ".";
            hostile.NumberFormat.NegativeSign = "~";

            // Gate on the precondition, not on a weaker form of the property (§XV.3): a clone that did not
            // take would leave every comparison below invariant against invariant, passing by asserting
            // nothing at all.
            string probe = 4.7.ToString("0.#", hostile);
            if (!Check("the probe culture really writes numbers differently", probe == "4,7", $"got \"{probe}\""))
                return;

            static bool Numeric(Type t) => t == typeof(double) || t == typeof(int) || t == typeof(long);

            static MethodInfo[] FormattersOf(Type t) => t
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public |
                            BindingFlags.DeclaredOnly)
                .Where(m => m.ReturnType == typeof(string) && !m.Name.Contains('<') &&
                            m.GetParameters().Length > 0 &&
                            m.GetParameters().All(p => Numeric(p.ParameterType) || p.HasDefaultValue))
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToArray();

            // Every formatter, with the surface it belongs to, so a failure names the file to open. Each
            // surface must still have one: a type that stops answering is a sweep that silently shrank.
            var found = Surfaces.ToDictionary(t => t, FormattersOf);
            string[] mute = found.Where(kv => kv.Value.Length == 0)
                                 .Select(kv => kv.Key.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            MethodInfo[] formatters = found.SelectMany(kv => kv.Value).ToArray();

            if (!Check($"every surface that states a number has formatters reachable by reflection " +
                       $"({Surfaces.Length} types, {formatters.Length} formatters)",
                       mute.Length == 0 && formatters.Length >= 20,
                       mute.Length > 0
                           ? $"{string.Join(", ", mute)} — no static formatter left to sweep"
                           : $"found only {formatters.Length}"))
                return;

            // Spread across every branch these have: under a k, under a M, a fraction, a whole, a unix
            // second. A single value would exercise one arm of a ternary and call the rest checked.
            double[] values = { 0, 0.5, 1.5, 4.7, 42, 99.5, 1234.5, 1_500_000, 1_700_000_000 };
            List<string> leaked = new(), threw = new();
            int compared = 0;

            foreach ((Type surface, MethodInfo[] ms) in found.Select(kv => (kv.Key, kv.Value)))
                foreach (MethodInfo m in ms)
                    foreach (double v in values)
                    {
                        object?[] args = m.GetParameters()
                            .Select(p => Numeric(p.ParameterType)
                                ? Convert.ChangeType(v, p.ParameterType)
                                : p.DefaultValue)
                            .ToArray();
                        string where = $"{surface.Name}.{m.Name}";
                        try
                        {
                            System.Globalization.CultureInfo.CurrentCulture =
                                System.Globalization.CultureInfo.InvariantCulture;
                            string a = (string)m.Invoke(null, args)!;
                            System.Globalization.CultureInfo.CurrentCulture = hostile;
                            string b = (string)m.Invoke(null, args)!;
                            compared++;
                            if (a != b) leaked.Add($"{where}({v:0.###}) → \"{a}\" / \"{b}\"");
                        }
                        catch (Exception e) { threw.Add($"{where}({v:0.###}): {(e.InnerException ?? e).Message}"); }
                    }

            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.InvariantCulture;

            // A throw is not a pass. A formatter that blew up on a probe was never compared, and counting
            // it as clean is the same silence as a Skip that hides what it guards.
            Check($"every formatter answered both cultures ({formatters.Length} × {values.Length})",
                  threw.Count == 0, string.Join("; ", threw.Take(4)));
            Check($"no formatter on any surface reads the OS number format ({compared} comparisons)",
                  leaked.Count == 0,
                  leaked.Count == 0 ? "" : $"{leaked.Count} leaked — {string.Join("; ", leaked.Take(6))}");

            // And the helper itself, by name, since everything above only says the surfaces agree with
            // themselves — they would all agree on the wrong convention just as quietly.
            System.Globalization.CultureInfo.CurrentCulture = hostile;
            Check("Nums keeps the decimal point a point", Nums.Of(4.7) == "4.7", Nums.Of(4.7));
            Check("Nums' whole form takes no group separator", Nums.Of(1234, "0") == "1234",
                  Nums.Of(1234, "0"));
            Check("Nums states a share without the space an invariant \"P0\" inserts",
                  Nums.Pct(0.271) == "27%", Nums.Pct(0.271));
            Check("Nums does not clamp a load past its window", Nums.Pct(1.4) == "140%", Nums.Pct(1.4));
            Check("the page's own helper still routes through it", StatisticsPage.Num(4.7) == "4.7",
                  StatisticsPage.Num(4.7));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }

    /// <summary>
    /// T340. Every state that has news says it, in every shipped language.
    ///
    /// <para>The tooltip has 127 characters and <c>Fit</c> takes the first rung that fits — or none,
    /// silently. That silence cost the same thing three times, each time in a different language and on
    /// a different sentence: T222's paying state in all five, T302's French billing states, T288's
    /// blocked sentence in es and fr. And each was locked shut by a test written for <em>that</em>
    /// state naming <em>that</em> key, so the check that would have caught the next one never existed
    /// until the next one happened.</para>
    ///
    /// <para>This is the cross product instead: every variant the catalogue has, against every language
    /// the app ships, asserting that the composition dropped nothing it had to say. Not a particular
    /// string and not a length limit on translations — a translator who needs more words should get
    /// them. The property is only that the news arrives, which is T222's own wording.</para>
    ///
    /// <para>It asks the composer rather than re-deriving the rule: walking the rungs here would be
    /// "the first that fits" written a second time, three lines from the method it duplicates, which is
    /// the exact shape T307 took out of that file.</para>
    /// </summary>
    private static void TooltipNews()
    {
        string[] codes = L.Codes.ToArray();
        IReadOnlyList<TooltipCli.Variant> variants = TooltipCli.Catalogue;
        const long Now = 1_800_000_000;

        L.Lang saved = L.Current;
        try
        {
            var lost = new List<string>();
            int said = 0;
            foreach (string code in codes)
            {
                L.Apply(code);
                foreach (TooltipCli.Variant v in variants)
                {
                    TooltipText.Compose(v.Build(Now), out IReadOnlyList<string> dropped);
                    said++;
                    foreach (string what in dropped)
                        lost.Add($"{code}/{v.Name}: {what}");
                }
            }
            Check($"no state loses a sentence it had, in any language ({said} combinations)",
                  lost.Count == 0,
                  $"{string.Join(", ", lost)} — the reading had something to say about that state and " +
                  "the cap took it, which is silent everywhere except here (T340)");
        }
        finally { L.Apply(L.Codes[(int)saved]); }
    }

    /// <summary>A month name formatted through whatever culture the machine happens to run under, in a
    /// window the user asked for in another language. Caught by looking at a published capture — an
    /// English shot reading <c>ago. 11</c> — which no assertion here was pointed at (T335).</summary>
    private static void DateCulture() =>
        Repo("every date the window formats goes through the UI language's culture", root =>
        {
            string ui = Path.Combine(root, "src", "Ui");
            var bare = new List<string>();
            foreach (string path in Directory.GetFiles(ui, "*.cs", SearchOption.AllDirectories))
            {
                string code = CodeOf(File.ReadAllText(path));
                foreach (Match m in Regex.Matches(code, @"ToString\(""[^""]*(MMM|dddd|ddd)[^""]*""(?<arg>[^)]*)\)"))
                    if (!m.Groups["arg"].Value.Contains("Culture", StringComparison.Ordinal))
                        bare.Add($"{Path.GetFileName(path)}: {m.Value}");
            }
            Check($"no month or weekday is formatted in the machine's own culture ({bare.Count} bare)",
                  bare.Count == 0,
                  $"{string.Join("; ", bare)} — a window in one language printing a month in another is " +
                  "what a published screenshot showed; pass L.DateCulture");
        }, "src");
}
