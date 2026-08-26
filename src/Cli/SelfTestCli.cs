using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeTray;

/// <summary>
/// `--selftest`: a deterministic self-check of the arithmetic behind the weekly projection (Block J), the
/// live rate (Block K) and the project-slug encoding both read from (T161), over synthetic inputs, exiting
/// non-zero on failure.
///
/// <para>Both blocks introduced properties that are cheap to assert and expensive to lose — a flat
/// profile must reproduce the straight average-pace line <em>exactly</em>, folding must be idempotent,
/// advice must never propose an hour that overshoots its own target, a burst must decay to a true zero
/// at exactly the window width — and every one of them was verified by a screenshot or a CLI read-out
/// by a human, once. Those are properties, not observations, and nothing stopped an edit from breaking
/// one silently.</para>
///
/// <para><b>Why in the binary and not a test project.</b> A test project means a third-party test
/// framework, which the non-goals rule out for a repo whose single self-contained <c>.exe</c> is a
/// feature (§I.3). This costs nothing at runtime, ships inside the same binary, and runs in CI as one
/// line. It is the same trade the fixtures already make (<see cref="ContextFixture"/>,
/// <see cref="ThroughputFixture"/>): synthetic input, real code path.</para>
///
/// <para><b>What it touches.</b> Everything is synthetic and removed afterwards: a temporary transcript
/// tree under the OS temp dir, and a <c>selftest</c> <em>profile</em> directory for the two stores that
/// have no in-memory seam (<see cref="HourlyUsage"/>). A real profile's stores are never read or
/// written — the same rule T128 set for fixture roots.</para>
/// </summary>
internal static partial class SelfTestCli
{
    /// <summary>The synthetic profile key. Not a hash like a real key (<c>acct-…</c>/<c>dir-…</c>), so it
    /// can never collide with an account's directory.</summary>
    private const string ProfileKey = "selftest";

    private static int _passed, _failed;
    private static readonly List<string> Failures = new();

    /// <summary>What did not run, by name (T169). The count alone cannot tell <em>"this run straddled a
    /// DST change"</em> from <em>"this environment has skipped these two forever"</em>, and the second is
    /// lost coverage that reads exactly like a clean run.</summary>
    private static readonly List<(string Name, string Why)> Skipped = new();

    /// <summary>
    /// The skips this suite is allowed to have, by name — so an <b>unexpected</b> one is a red run and a
    /// known one is not (T218).
    ///
    /// <para>T169 made every skip print its name and deliberately left this policy open: whether an
    /// unexpected skip should fail the build was "a judgement to make with the list in hand", and the
    /// list now exists. Until now <c>--selftest</c> exited 0 with any number of skips and
    /// <c>check.yml</c> read only the exit code, so a green run and a green run that checked two fewer
    /// things were the same colour — which is T169's own defect moved from one guard to the whole
    /// suite.</para>
    ///
    /// <para><b>Failing on any skip would be wrong.</b> Two of these are genuinely conditional on the
    /// machine and the moment, so a blunt rule makes the suite flaky, and a flaky suite gets ignored —
    /// which costs more coverage than the skips do. A count (<c>--max-skips</c>) is the cheap form and
    /// nobody maintains a number; naming them means a skip that <em>stops</em> happening is as visible as
    /// one that starts, which is why the reasons are here rather than in a comment.</para>
    ///
    /// <para>Each entry says what it costs to allow it. A skip that is <em>always</em> expected where this
    /// runs is a check that does not exist there, and the honest response is to read that line and decide,
    /// not to let the exit code stay quiet about it.</para>
    /// </summary>
    private static readonly (string Name, string Why)[] AllowedSkips =
    {
        ("the resume hour closes the week under its own target",
         "the synthetic week does not always produce a profile whose resume hour lands under its target"),
        ("the probe backtracks past a shorter directory that exists",
         "a temp path whose encoding cannot be rebuilt - unrepresentable, not an unwritten case (T169)"),
        ("the probe returns only directories that exist",
         "the same temp path, in the same run"),
        ("no file under %LocalAppData%\\ClaudeTray was created or changed",
         "another ClaudeTray writes that tree on its own timer, so a change cannot be attributed to "
         + "this run - the counted assertion beside it is the one that answers the promise (T356). "
         + "This skip is expected on a developer's machine and must NOT happen on CI, where no tray "
         + "is resident: if it fires there, something started one."),
        // The mirror of the one above, and the reason these carry a sentence rather than a name (T387).
        ("a plan needing a file symlink refuses, and moves nothing",
         "elevation satisfies the emitted preflight on its own, so no registry key can make that branch "
         + "fire and the refusal cannot be reached (T386). Expected on CI, whose Windows runner is an "
         + "administrator, and must NOT happen on a developer's machine: there it means the suite was "
         + "run from an elevated shell and the one assertion this project has that is WEAKER on CI went "
         + "unchecked. Dropping a privilege mid-run to reach it is not something a check may do."),
        // The repository family is not here on purpose (T247): `Repo` carries its own allowance, so the
        // seven sentences that used to sit in this list are one, and the eighth check inherits it.
    };

    /// <summary>
    /// Whether a source file is part of <b>this suite</b> — by stem, so all six of its files answer yes
    /// (T381).
    ///
    /// <para>Two source-scanning checks skip the suite, because a scanner naming the string it looks for
    /// reports itself. Both were keyed on the filename <c>SelfTestCli.cs</c>, and the split turned one of
    /// them red — a correct check failing on a file move rather than on a defect, which is the fastest way
    /// to teach somebody that a check is noise. Keyed on the stem, the same rule the file map already uses
    /// to resolve a partial to its row, adding a seventh file costs nothing.</para>
    /// </summary>
    private static bool IsSuite(string path) =>
        Path.GetFileName(path).StartsWith("SelfTestCli.", StringComparison.OrdinalIgnoreCase);

    /// <summary>The last line of a scanned source that still carries code, trimmed. A file whose own is
    /// the class's closing brace is one the scan read to the end of.</summary>
    private static string LastCode(string code) =>
        code.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";

    /// <returns>Process exit code: 0 when every check passed.</returns>
    public static int Run(string[] flags)
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        bool quick = flags.Contains("--quick");
        long started = DateTime.UtcNow.Ticks;

        Console.WriteLine("Claude Code Tray — self-check (synthetic inputs, no real profile is read or written)");
        Console.WriteLine();

        Section("pacing — the shape and the staircase (Block J)");
        Pacing();

        Section("kernel — the live rate (Block K)");
        Kernel();

        Section("poll — when the idle is right, and when it is a hole in the history (Block AE)");
        PollIdle();

        Section("states — in the quota, past it and billing, stopped (Block AE)");
        States();

        Section("stores — folding, the ghost, intensity (Block J)");
        string dir = ProfileStore.DirFor(ProfileKey);
        Console.WriteLine($"  scratch profile: {dir}");
        Remove(dir);            // a crashed earlier run must not decide what "already folded" means
        try
        {
            Stores();
            Section("overage — a reading with somewhere to be written (Block AE)");
            Overage();

            Section("probe — the headers verbatim, recorded on a change (Block AE)");
            Probe();
        }
        catch (Exception e) { Fail("the section threw", e.Message); }
        finally { Remove(dir); }

        Section("sweep — the transcript grid and the away-week exclusion (Block J)");
        Temp(Sweep);

        Section("slug — the projects/<slug> encoding (Blocks K and W)");
        Temp(Slug);

        Section("settings — the page's copy and the tray's carry (Blocks S and Z)");
        SettingsRoundTrip();

        Section("lang — five files, one key set and one set of holes (Block AF)");
        Translations();

        Section("keys — every key the code names is one the table holds (Block AI)");
        KeyScanning();
        SpelledKeys();

        Section("flags — the preview and capture surface (Block AI)");
        Flags();

        Section("tooltip — what fits in the tray's 127 characters (Block AI)");
        Tooltip();

        Section("spell — how far back the overage reaches (Block A)");
        Spell();

        Section("switch — what a change of monitored account drops (Block A)");
        MonitoredHandover();

        Section("picker — the report follows the icon by key, not by position (Block F)");
        ProfilePicker();

        Section("frame — what a capture says about what it framed (Block AI)");
        Framing();

        Section("out — the directory a capture flag was given (Block AF)");
        Temp(OutputPaths);

        Section("toasts — one colour vocabulary, one fact per colour (Block AF)");
        ToastColours();

        Section("series — what the chart is handed, from readings alone (Block AF)");
        Series();

        Section("names — one automation id, one control (Block AG)");
        AutomationIds();

        Section("effective — which profile the environment selects (Block AC)");
        EffectiveProfile();

        Section("follow — two profiles behind one transcript tree are not evidence (Block O)");
        Temp(Follow);

        Section("link — the script that makes two profiles one setup (Block O)");
        Temp(LinkScript);

        Section("link — every branch of the script, in a script (Block AI)");
        Temp(ScriptBranches);

        // After the text checks, because it is the same artifact and this is the expensive half: three
        // PowerShell launches, which `--quick` has no business paying for.
        if (!quick)
        {
            Section("link — the emitted script run under Windows PowerShell (Block AI)");
            Temp(LinkRun);
        }

        Section("probe — what the flag was asked to do, before it does any of it (Block AI)");
        ProbePlanning();

        Section("probe — which arriving headers this app actually reads (Block AI)");
        Readership();

        Section("glyphs — every card's emoji is in the font the card names (Block E)");
        ToastGlyphs();

        Section("the extra-usage card — a bar only for a figure it has (Block E)");
        ExtraCardBar();

        Section("the extra-usage alarm — a seed, a rise, and one announcement per spell (Block E)");
        ExtraAlarm();

        Section("projects — a count of directories, not of keys (Block N)");
        ProjectCount();

        Section("note — which paragraphs the method note yields (Block F)");
        MethodNote();

        Section("format — one number convention, on every surface that states one (Blocks F and G)");
        Formatting();

        Section("ledger — every block heading has a row in its own index (Block AJ)");
        LedgerIndex();

        Section("map — every source file has a row, and every row a file (Block AJ)");
        FileMap();

        Section("intensity — the line that names its own axis (Block J)");
        IntensityAxis();

        Section("warnings — the gate that keeps a build log worth reading (Block AI)");
        WarningGate();

        Section("sdk — one pin, and every build resolves through it (Block B)");
        SdkPin();

        Section("report — one label, one quantity (Block I)");
        ReportLabels();

        Section("prompt — what the cleanup prompt carries out of the app (Block I)");
        CleanupPrompt();

        Section("fixture — the overage preview produces the state it exists for (Block AI)");
        OveragePreview();

        Section("dates — the fields are in the order the culture puts them (Block G)");
        DateOrder();

        Section("names — a page or destination this build does not have is refused (Block AI)");
        PageNames();

        Section("read-out — a scan that could not start exits 1 (Block I)");
        ReadOutExit();

        Section("lang — a code this build does not ship is refused, not defaulted (Block AI)");
        LangOverride();

        Section("scripts — every .ps1 is one PowerShell 5.1 can read (Block AI)");
        ScriptEncoding();

        Section("source — no file that git would call binary (Block AI)");
        SourceIsText();

        Section("source and lang — no text that survived a round trip through CP1252 (Block AI)");
        SourceIsNotDoubleEncoded();

        Section("console — the code page is set where the flags are dispatched (Block AI)");
        ConsoleCodePage();

        Section("source — what a scan of this repository reads as code (Block AI)");
        CodeReading();

        Section("parser — the names it spells and the names it reports are one set (Block AI)");
        ParserNames();

        Section("scanning — the flag scan reads code, not the prose about it (Block AI)");
        FlagScanning();

        Section("catalogue — every flag the sources accept is declared (Block AJ)");
        FlagCatalogue();

        Section("cache — the write, at the TTL it was written for (Block AK)");
        CacheTtl();

        Section("list prices — what a conversation was worth, per model (Block AK)");
        ListPricing();

        Section("effort — the lever a turn ran at, kept as a mix (Block AK)");
        Effort();

        Section("peak — the heaviest five hours, swept from the index (Block AK)");
        Heaviest();

        Section("kinds — which kind of work ate the range (Block AK)");
        Kinds();

        Section("fixture — the Sessions pane a published shot may come from (Block AI)");
        SessionsFixture();

        Section("dates — a month name in the language the window is in (Block AI)");
        DateCulture();

        Section("running — which conversation a spike on the strip belongs to (Block AK)");
        RunningSession();

        Section("layout — trimming that can never fire (Block AI)");
        TrimmingThatCannotFire();

        Section("tooltip — every state's news, in every language (Block AI)");
        TooltipNews();

        Section("anchoring — a transcript reached by a route nobody walked (Block AK)");
        Anchoring();

        Section("surfaces — what a stranger reads: the README and the page (Block AJ)");
        Repo("the README and the published page point at things that exist", UserSurfaces,
             "README.md", "site/src/lib/site-content.ts", "site/public/shots");

        if (quick)
        {
            Console.WriteLine();
            Console.WriteLine("tail — skipped (--quick); it waits on real sweeps and takes a few seconds");
        }
        else
        {
            Section("tail — the byte cursor (Block K)");
            Temp(Tail);

            Section("tail — the primed cursor over a large transcript (Block K)");
            Temp(Primed);

            Section("tail — where a fan-out's agents file (Block K)");
            Temp(FanOut);

            Section("tail — what 'sessions active' counts (Block K)");
            Temp(Active);

            Section("sessions — one row per conversation (Block AK)");
            Temp(Sessions);

            Section("sessions — the one field a person wrote (Block AK)");
            Temp(OpeningPrompt);

            Section("sessions — the call tree under one conversation (Block AK)");
            Temp(Tasks);
        }

        // Last, and nothing may be added after it: sampling the environment is one-way for the process
        // (T231), so every section that reads the machine's real CLAUDE_CONFIG_DIR has to be above.
        Section("sampled environment — the states this machine is never in (Block AI)");
        SampledEnvironment();

        // After the sampling section and after everything else: Observe() is one-way too, and it is the
        // stricter of the two — from here on this process writes nothing at all (T239).
        Section("observing tray — a check that adds nothing to the user's files (Block AI)");
        StoreRootSpelledOnce();
        OnlyTheTrayMigrates();
        ObservingGateCallSites();
        ObservingTray();

        double ms = (DateTime.UtcNow.Ticks - started) / (double)TimeSpan.TicksPerMillisecond;
        Console.WriteLine();
        foreach (string f in Failures) Console.WriteLine("FAILED  " + f);

        // Beside the counts, where the exit code is read: a name is what makes lost coverage legible.
        // Each one says what allowing it costs, so the line is a decision and not a note (T218).
        foreach ((string name, string why) in Skipped)
        {
            string? allowed = AllowedSkips.FirstOrDefault(a => a.Name == name).Why
                              ?? (RepoSkipped.Contains(name) && !OnCi ? "an installed copy carries no repository (T247)" : null);
            Console.WriteLine($"SKIPPED {name} — {why}");
            if (allowed is not null) Console.WriteLine($"        allowed: {allowed}");
        }

        (string Name, string Why)[] unexpected = Skipped
            .Where(s => AllowedSkips.All(a => a.Name != s.Name))
            .Where(s => !RepoSkipped.Contains(s.Name) || OnCi)   // allowed off a checkout, never on CI (T247)
            .ToArray();
        foreach ((string name, string why) in unexpected)
            Console.WriteLine($"UNEXPECTED SKIP  {name} — {why}: nobody decided to lose this one. " +
                              (RepoSkipped.Contains(name)
                                  ? "CI runs this build from the checkout, so the repository is here and " +
                                    "RepoFile stopped finding it (T247)."
                                  : "Fix the reason, or add it to AllowedSkips with what allowing it costs."));

        // The other direction, and the reason this is a list of names rather than a number: an allowed
        // skip that has stopped happening is coverage regained, or a check that has quietly been renamed
        // out from under this list. Reported, never failed — most of these are absent on most machines.
        string[] absent = AllowedSkips.Where(a => Skipped.All(s => s.Name != a.Name))
                                      .Select(a => a.Name).ToArray();
        if (absent.Length > 0)
            Console.WriteLine($"({absent.Length} allowed skip(s) did not happen here: {string.Join("; ", absent)})");

        Console.WriteLine($"{_passed} passed, {_failed} failed" +
                          (Skipped.Count > 0 ? $", {Skipped.Count} skipped" : "") +
                          (unexpected.Length > 0 ? $", {unexpected.Length} UNEXPECTED" : "") + $" — {ms:0}ms");
        return _failed == 0 && unexpected.Length == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- Block J: the projection

    /// <summary>Two distinct values for a property, both of which survive <see cref="Settings.Clone"/>'s
    /// clamping — null when this property's type (or validator) has no rule here, which the caller reports
    /// as a failure rather than skipping.</summary>
    private static (object? edited, object? moved)? Vary(PropertyInfo p)
    {
        (object? a, object? b)? pair = p.Name switch
        {
            // Validated values: an arbitrary string would be clamped back to the default, and a check
            // over a value the model rejects asserts nothing.
            nameof(Settings.Metric) => ("7d", "extra"),
            nameof(Settings.Language) => ("en", "fr"),
            nameof(Settings.Profiles) => (Profiles(@"d:\one"), Profiles(@"d:\two")),
            _ => p.PropertyType switch
            {
                var t when t == typeof(bool) => (false, true),
                // Off the default rather than from thin air: every int here is clamped to a range the
                // default sits inside, so default±1 is in range without this knowing the range.
                var t when t == typeof(int) => Ints(p),
                var t when t == typeof(string) => ("alpha", "beta"),
                _ => ((object?, object?)?)null,
            },
        };
        if (pair is not { } both) return null;
        (object? a, object? b) = both;

        // Only claim a pair the model actually keeps: setting a value the clamp rewrites would make the
        // check pass or fail for a reason that has nothing to do with the carry.
        foreach (object? v in new[] { a, b })
        {
            var probe = new Settings();
            p.SetValue(probe, v);
            if (Json(p.GetValue(probe.Clone())) != Json(v)) return null;
        }
        return (a, b);
    }

    private static (object? a, object? b) Ints(PropertyInfo p)
    {
        int d = (int)p.GetValue(new Settings())!;
        return (d + 1, d + 2);
    }

    private static List<ClaudeProfile> Profiles(string dir) => new() { new ClaudeProfile { ConfigDir = dir } };

    private static string Json(object? v) => System.Text.Json.JsonSerializer.Serialize(v);

    // ---------------------------------------------------------------- Block AF: the chart's series

    // A failure detail that names the keys instead of counting them, capped so one forgotten file cannot
    // bury the rest of the run.
    private static string Named(string[] keys, string what)
    {
        if (keys.Length == 0) return "";
        string list = string.Join(", ", keys.Take(12));
        return $"{keys.Length} {(keys.Length == 1 ? "key" : "keys")} {what} — {list}" +
               (keys.Length > 12 ? $", … (+{keys.Length - 12} more)" : "");
    }

    // ------------------------------------------------- Block AI: the keys the code names, against the table

    /// <summary>
    /// One entry per <c>L.T(…)</c> in the source — the string literals in its <b>first argument</b>, over
    /// code with its comments already removed (<see cref="CodeOf"/>). An empty entry is a call whose key is
    /// built at run time, which is what lets the caller count what it cannot see.
    ///
    /// <para>The first argument rather than the opening literal, because the call this exists for is
    /// <c>L.T(_remaining ? "stats.stat.left" : "stats.stat.used")</c> — a ternary, or a <c>switch</c>
    /// expression, whose branches are keys a regex anchored on <c>L.T("</c> never sees — measured over this
    /// tree, that anchor reaches 497 of the 564 keys, and the 67 it misses are written this way, the
    /// legend's among them, which is the shape T313 introduced. Stopping at the first comma is what keeps a
    /// format argument out: <c>L.T(k, "N/A")</c> names one key, not two.</para>
    ///
    /// <para>A literal carrying <c>$</c>, <c>@</c> or a raw fence is stepped over and not collected — an
    /// interpolated key is built at run time — but it is stepped over with the terminator its own form
    /// uses, because the failure that costs is the other one: a literal whose end is read wrong runs the
    /// walk past the call and reports every string after it as a key.</para>
    /// </summary>
    internal static IEnumerable<string[]> KeysNamed(string code)
    {
        foreach (Match call in LocCall.Matches(code))
        {
            var keys = new List<string>();
            int i = call.Index + call.Length, depth = 1;
            while (i < code.Length && depth > 0)
            {
                char c = code[i];

                // A prefix decides how the literal ends, so it is read before the quote rather than guessed
                // after one — the same order CodeOf reads them in.
                int p = i;
                bool verbatim = false, decorated = false;
                while (p < code.Length && (code[p] == '@' || code[p] == '$'))
                {
                    verbatim |= code[p] == '@';
                    decorated = true;
                    p++;
                }
                if (p < code.Length && code[p] == '"')
                {
                    int quotes = 0;
                    while (p + quotes < code.Length && code[p + quotes] == '"') quotes++;
                    int end = quotes >= 3 ? RawStringEnd(code, p + quotes, quotes)
                            : verbatim ? VerbatimEnd(code, p + 1)
                            : QuotedEnd(code, p + 1);
                    if (!decorated && quotes == 1 && end - 1 > p) keys.Add(code[(p + 1)..(end - 1)]);
                    i = end;
                    continue;
                }
                if (decorated) { i = p; continue; }

                if (c == '\'') { i = CharEnd(code, i + 1); continue; }
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth--;
                else if (c == ',' && depth == 1) break;
                i++;
            }
            yield return keys.ToArray();
        }
    }

    /// <summary>A refusal prints its whole catalogue, which is the right behaviour for a person at a
    /// prompt and noise in the middle of a self-check. Runs one and keeps the return value.</summary>
    private static T Quietly<T>(Func<T> f)
    {
        TextWriter saved = Console.Out;
        try { Console.SetOut(TextWriter.Null); return f(); }
        finally { Console.SetOut(saved); }
    }

    // ---------------------------------------------------------------- Block AJ: the ledger's own index

    /// <summary>
    /// One source file's <b>code</b>: every comment removed, every string literal kept whole (T285).
    ///
    /// <para><b>Why this exists.</b> Three checks here read this repository's own sources. <c>FlagsRead</c>
    /// (T248) matches a comparison — <c>Contains("--x")</c> — so a flag quoted in a paragraph is not a flag
    /// the app accepts. The two added since matched a bare literal and then subtracted prose by hand, and
    /// both paid for it on their first run: T283's scan read its own summary, which names the setter in
    /// order to explain what it counts, and T284's needed a second rule because the paragraph above the
    /// parse writes the family with a trailing dash. Each hand rule is right about the case that produced
    /// it and blind to the next one — and a scan whose exclusion is approximate fails by asserting
    /// <em>less</em>, which still passes.</para>
    ///
    /// <para><b>So the question is answered once, lexically, instead of by pattern.</b> A comment is not a
    /// line that looks like one; it is a region C# would not compile. Strings are kept because they are
    /// what the scans are looking for — a header name and a flag are both literals — and they are the whole
    /// reason stripping to the first <c>//</c> is wrong: <c>"https://x"</c> would end the line.</para>
    ///
    /// <para>Every literal form this repository actually contains is handled, raw strings included, because
    /// this file has them: desynchronising on <c>$$"""…"""</c> would silently skip the rest of the largest
    /// source here and answer with confidence. Newlines are preserved so a caller may still count by line.</para>
    /// </summary>
    internal static string CodeOf(string source)
    {
        var code = new StringBuilder(source.Length);
        int i = 0;

        // Emit the newlines inside a skipped region, so nothing that counts lines is thrown off by a
        // comment or a multi-line string being removed.
        void Skip(int from, int to)
        {
            for (int k = from; k < to && k < source.Length; k++)
                if (source[k] == '\n') code.Append('\n');
        }

        while (i < source.Length)
        {
            char c = source[i];

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                int end = source.IndexOf('\n', i);
                if (end < 0) break;
                Skip(i, end);
                i = end;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                Skip(i, end);
                i = end;
                continue;
            }

            // A string may carry any of $, @ and $$ in front of it. The prefix decides how it ends, so it
            // is read before the quote rather than guessed after one.
            int p = i;
            bool verbatim = false, interpolated = false;
            while (p < source.Length && (source[p] == '@' || source[p] == '$'))
            {
                verbatim |= source[p] == '@';
                interpolated |= source[p] == '$';
                p++;
            }
            if (p < source.Length && source[p] == '"')
            {
                int quotes = 0;
                while (p + quotes < source.Length && source[p + quotes] == '"') quotes++;
                int end = quotes >= 3
                    ? RawStringEnd(source, p + quotes, quotes)
                    : verbatim ? VerbatimEnd(source, p + 1) : QuotedEnd(source, p + 1);
                code.Append(source, i, end - i);
                i = end;
                continue;
            }
            _ = interpolated;   // read for the prefix, not needed once the terminator is known

            if (c == '\'')
            {
                int end = CharEnd(source, i + 1);
                code.Append(source, i, end - i);
                i = end;
                continue;
            }

            code.Append(c);
            i++;
        }

        return code.ToString();
    }

    /// <summary>Past the closing quote of a <c>"…"</c>, where a backslash escapes the next character.</summary>
    private static int QuotedEnd(string s, int i)
    {
        while (i < s.Length)
        {
            if (s[i] == '\\') { i += 2; continue; }
            if (s[i] == '"' || s[i] == '\n') return i + 1;
            i++;
        }
        return s.Length;
    }

    /// <summary>Past the closing quote of an <c>@"…"</c>, where <c>""</c> is one quote and a backslash is
    /// an ordinary character — the form every regex in this file is written in.</summary>
    private static int VerbatimEnd(string s, int i)
    {
        while (i < s.Length)
        {
            if (s[i] != '"') { i++; continue; }
            if (i + 1 < s.Length && s[i + 1] == '"') { i += 2; continue; }
            return i + 1;
        }
        return s.Length;
    }

    /// <summary>Past the closing fence of a raw string: the first run of at least as many quotes as opened
    /// it. A shorter run is content, which is the entire point of the form.</summary>
    private static int RawStringEnd(string s, int i, int opened)
    {
        while (i < s.Length)
        {
            if (s[i] != '"') { i++; continue; }
            int run = 0;
            while (i + run < s.Length && s[i + run] == '"') run++;
            if (run >= opened) return i + run;
            i += run;
        }
        return s.Length;
    }

    /// <summary>Past the closing quote of a char literal, so <c>'"'</c> does not open a string.</summary>
    private static int CharEnd(string s, int i)
    {
        while (i < s.Length)
        {
            if (s[i] == '\\') { i += 2; continue; }
            if (s[i] == '\'' || s[i] == '\n') return i + 1;
            i++;
        }
        return s.Length;
    }

    /// <summary>The flag literals one source file compares against. Pure, so <see cref="FlagScanning"/> can
    /// hand it the two lines that used to be read wrong.</summary>
    internal static IEnumerable<string> FlagsRead(string source) =>
        FlagComparison.Matches(source).Select(m => m.Groups["flag"].Value);

    /// <summary>A file's stem row: everything before the first dot, plus <c>.cs</c> — so
    /// <c>StatisticsPage.Throughput.cs</c> and <c>MainWindow.xaml.cs</c> both resolve to the row that names
    /// the type they are part of. One class in several files is this repository's convention (T133–T134),
    /// not an omission from the map.</summary>
    private static string Stem(string relative)
    {
        string dir = relative[..(relative.LastIndexOf('/') + 1)];
        string name = relative[dir.Length..];
        int dot = name.IndexOf('.');
        return dot < 0 ? relative : dir + name[..dot] + ".cs";
    }

    /// <summary>A heading's GitHub anchor: lowercased, everything that is not a letter, a digit, a space
    /// or a hyphen dropped, then spaces to hyphens. Verified against all thirty-six rows the ledger
    /// already carries, em dashes and apostrophes included.</summary>
    private static string Anchor(string heading)
    {
        var sb = new StringBuilder();
        foreach (char c in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c is ' ' or '-') sb.Append('-');
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- Block AG: one id, one control

    /// <summary>
    /// Run one <c>.ps1</c> under <b>Windows PowerShell 5.1</b> and return its exit code with everything it
    /// printed. The absolute path to <c>powershell.exe</c> rather than the name, because <c>pwsh</c> may be
    /// first on <c>PATH</c> and 7.x is the edition where neither defect this check exists for reproduces.
    /// </summary>
    private static (int, string) PowerShell(string script, bool apply)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(
            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script })
            psi.ArgumentList.Add(a);
        if (apply) psi.ArgumentList.Add("-Apply");

        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return (-1, "powershell.exe did not start");
            string text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            if (!p.WaitForExit(60_000)) { try { p.Kill(true); } catch { } return (-1, "timed out: " + text); }
            return (p.ExitCode, text);
        }
        catch (Exception e) { return (-1, e.Message); }
    }

    /// <summary>A script's lines that are commands rather than comments.</summary>
    private static IEnumerable<string> Acting(string script) =>
        script.Split('\n').Where(l => !l.TrimStart().StartsWith("#", StringComparison.Ordinal));

    /// <summary>
    /// A script's lines that are <b>this app's own words</b> — everything except the ones quoting the
    /// user's permission rules verbatim (T380).
    ///
    /// <para>The distinction is not fussiness. <c>Bash(ls)</c>, <c>Read(/src/**)</c> and
    /// <c>WebFetch(domain:x)</c> are exactly the parenthesised shape T378 refuses, they are common, and they
    /// are not ours to rewrite. The branch sweep found this the first time it ran — the check was right
    /// about the shape and wrong about whose text it was reading, on a fixture whose rules happened to be
    /// <c>Bash(a)</c> and <c>Bash(b)</c>.</para>
    ///
    /// <para>Keyed on <see cref="SettingsUnion.Quoted"/> rather than on an indent counted here, so the
    /// marker the script prints and the exemption this takes cannot drift apart.</para>
    /// </summary>
    private static IEnumerable<string> Ours(string script) =>
        script.Split('\n').Where(l => !l.StartsWith(SettingsUnion.Quoted, StringComparison.Ordinal));

    /// <summary>
    /// Six pairs of config dirs, each shaped for the branches the others cannot reach: everything acting,
    /// everything already linked, nothing on the secondary, nothing on the primary, two settings files that
    /// agree, and one that only one side has.
    /// </summary>
    private static List<(string, ProfileLink.Plan)> BranchPlans(string root)
    {
        string[] dirs = { "projects", "file-history", "skills", "agents", "commands", "output-styles", "plugins" };
        string[] files = { "history.jsonl", "CLAUDE.md" };
        var plans = new List<(string, ProfileLink.Plan)>();

        // 1. Everything acting, settings that disagree, one unclaimed entry: the ordinary case and most of
        //    the labels.
        (string a, string b) = Pair(root, "acting");
        foreach (string d in dirs) { Dir(a, d); Dir(b, d); }
        foreach (string f in files) { File.WriteAllText(Path.Combine(a, f), "{}\n"); File.WriteAllText(Path.Combine(b, f), "{}\n"); }
        Dir(b, "projects", "only-here");
        Dir(b, "an-entry-nobody-has-an-opinion-about");
        File.WriteAllText(Path.Combine(a, "settings.json"), """{ "permissions": { "allow": ["Bash(a)"] } }""");
        File.WriteAllText(Path.Combine(b, "settings.json"), """{ "permissions": { "allow": ["Bash(b)"] } }""");
        plans.Add(("acting", ProfileLink.For(a, b, "Keeps", "Links")));

        // 2. Every directory already a junction into the primary. Also the plan with no edge and, because
        //    `projects` is linked, the one that costs auto-follow nothing.
        (a, b) = Pair(root, "linked");
        foreach (string d in dirs) { Dir(a, d); }
        int junctions = 0;
        foreach (string d in dirs) if (Junction(Path.Combine(b, d), Path.Combine(a, d))) junctions++;
        plans.Add(("already-linked", ProfileLink.For(a, b, "Keeps", "Links")));

        // 3. Directories on the primary only: link with nothing to merge first, and no file anywhere, so
        //    no symlink is needed at all.
        (a, b) = Pair(root, "link-only");
        foreach (string d in dirs) Dir(a, d);
        plans.Add(("link-only", ProfileLink.For(a, b, "Keeps", "Links")));

        // 4. The mirror: everything on the secondary, nothing on the primary to link into.
        (a, b) = Pair(root, "absent-primary");
        foreach (string d in dirs) Dir(b, d);
        foreach (string f in files) File.WriteAllText(Path.Combine(b, f), "{}\n");
        plans.Add(("absent-primary", ProfileLink.For(a, b, "Keeps", "Links")));

        // 5. Two settings files that say the same thing — the branch the one-plan scan was blind on.
        (a, b) = Pair(root, "settings-same");
        Dir(a, "projects");
        File.WriteAllText(Path.Combine(a, "settings.json"), "{}\n");
        File.WriteAllText(Path.Combine(b, "settings.json"), "{}\n");
        plans.Add(("settings-same", ProfileLink.For(a, b, "Keeps", "Links")));

        // 6. A settings file only one side has, which is the widest form of that decision rather than the
        //    emptiest — and the reading reports it as an error rather than a union.
        (a, b) = Pair(root, "settings-one-sided");
        Dir(a, "projects");
        File.WriteAllText(Path.Combine(a, "settings.json"), "{}\n");
        plans.Add(("settings-one-sided", ProfileLink.For(a, b, "Keeps", "Links")));

        if (junctions < dirs.Length)
            Skip("the already-linked branch over a real reparse point",
                 $"only {junctions} of {dirs.Length} junctions could be created here");
        return plans;
    }

    private static (string, string) Pair(string root, string name)
    {
        string a = Path.Combine(root, name, "keeps"), b = Path.Combine(root, name, "links");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        return (a, b);
    }

    /// <summary>
    /// Every branch the script has, enumerated from the enums rather than listed — so a fifth verdict
    /// demands a fixture on the day it is added instead of passing under the four that exist.
    /// </summary>
    private static IEnumerable<string> Required()
    {
        foreach (ProfileLink.Verdict v in Enum.GetValues<ProfileLink.Verdict>())
            foreach (string state in States(v)) yield return $"{v}/{state}";
        foreach (ProfileLink.Union u in Enum.GetValues<ProfileLink.Union>()) yield return $"union/{u}";
        foreach (string f in new[] { "symlink", "no-symlink", "auto-follow", "no-auto-follow", "edge", "no-edge" })
            yield return $"plan/{f}";
    }

    private static IEnumerable<string> Branches(ProfileLink.Plan plan)
    {
        foreach (ProfileLink.Step s in plan.Steps)
        {
            yield return Label(s);
            // The union label only where the merge is actually emitted: an entry that is skipped carries a
            // union nothing called.
            if (s.Entry.Verdict == ProfileLink.Verdict.Merge && s.OnPrimary && s.OnSecondary && !s.AlreadyLinked)
                yield return $"union/{s.Entry.Union}";
            if (s.Entry.Union == ProfileLink.Union.None) yield return "union/None";
        }
        yield return plan.NeedsSymlink ? "plan/symlink" : "plan/no-symlink";
        yield return plan.CostsAutoFollow ? "plan/auto-follow" : "plan/no-auto-follow";
        yield return plan.Edge.Unclaimed.Length > 0 ? "plan/edge" : "plan/no-edge";
    }

    /// <summary>How many times one literal appears in a text. Counted rather than merely found, because
    /// "written once" is the claim (T379) and <c>Contains</c> is as true of nine copies as of one.</summary>
    private static int Occurrences(string text, string needle)
    {
        int n = 0;
        for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>Make a directory junction, or answer false where the environment will not have one.
    /// <c>mklink /J</c> rather than <see cref="Directory.CreateSymbolicLink"/>, which needs Developer
    /// Mode or an elevated process — a junction is the link the case being tested is actually made
    /// of.</summary>
    private static bool Junction(string link, string target)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                });
            p?.WaitForExit(10_000);
            return p is { ExitCode: 0 } && Directory.Exists(link);
        }
        catch { return false; }
    }

    /// <summary>The line declaring the member a line sits inside, or -1. The same backwards read
    /// <see cref="MonitoredHandover"/> uses: the nearest preceding line that opens a member — a modifier, a
    /// parameter list, and no <c>;</c> ending it, which is what separates a declaration from a field whose
    /// initialiser happens to call something.</summary>
    private static int DeclaringMember(string[] lines, int at)
    {
        for (int i = at; i >= 0; i--)
        {
            string t = lines[i].TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal)) continue;
            if (!t.StartsWith("private ", StringComparison.Ordinal)
                && !t.StartsWith("internal ", StringComparison.Ordinal)
                && !t.StartsWith("public ", StringComparison.Ordinal)
                && !t.StartsWith("protected ", StringComparison.Ordinal)
                && !t.StartsWith("static ", StringComparison.Ordinal)) continue;
            if (!t.Contains('(') || t.TrimEnd().EndsWith(";", StringComparison.Ordinal)) continue;
            return i;
        }
        return -1;
    }

    /// <summary>The identifier a declaration line declares: the word before its parameter list.</summary>
    private static string MemberName(string declaration)
    {
        string head = declaration[..declaration.IndexOf('(')];
        int space = head.LastIndexOfAny(new[] { ' ', '\t', '>', '.' });
        return space < 0 ? head.Trim() : head[(space + 1)..].Trim();
    }

    /// <summary>
    /// The precondition every *one claim, two places* check shares, named once (T247).
    ///
    /// <para>Seven assertions had grown the same four lines each — call <see cref="RepoFile"/>, get null on
    /// an installed copy, <c>Skip</c> with a freshly written sentence, and remember to add a matching line
    /// to <see cref="AllowedSkips"/> so the skip is not red. The duplication was not the cost. Seven
    /// sentences said one thing seven ways; the allowance was remembered in a second place, where a name
    /// typed differently is a skip that goes red for a reason nobody can act on; and the eighth check would
    /// inherit none of it. Here the allowance travels <em>with</em> the skip, so a check declares its
    /// subject and nothing else, and the family is one sentence.</para>
    ///
    /// <para>It also hands over the repository <b>root</b>, which nothing had: <c>RepoFile</c> answers about
    /// files, so the check that needed the <c>src\</c> directory derived it from wherever <c>AGENTS.md</c>
    /// happened to be found. A file the checkout is missing is now a red precondition inside the body
    /// rather than a skip — in a repository those files are always there, and pretending otherwise is how a
    /// check quietly stops asserting.</para>
    ///
    /// <para><b>The allowance is for an installed copy and nowhere else</b>, which is what §XXII.8 left to
    /// settle. <c>check.yml</c> runs the build from the checkout, so a repository skip there is
    /// <c>RepoFile</c> having broken — five checks lost in a run that stays green, which is the exact shape
    /// this file keeps naming. Under <c>CI</c> the allowance does not apply and the skip is unexpected.</para>
    ///
    /// <para><b><c>needs</c> is what the check reads (T251).</b> Naming the family's guard once left every
    /// body opening with a guard of its own — combine a path, <c>Check</c> it exists, return — which is the
    /// same shape one level down, seven times, and seven assertions that pass every run and have never
    /// been seen to fail. Declared here they are proved in one place, and the assertion says which path is
    /// missing rather than that some file was.</para>
    ///
    /// <para>§XXII.11 asked whether a governed file the checkout lacks should be red or the same skip as a
    /// missing repository. <b>Red.</b> The two are not the same fact: an installed copy has no repository
    /// and never will, while a checkout missing <c>CHANGELOG.md</c> is a repository somebody is halfway
    /// through changing — and standing a check down for that is how coverage is lost to a state nobody
    /// meant to leave behind.</para>
    /// </summary>
    private static void Repo(string claim, Action<string> assert, params string[] needs)
    {
        string? agents = RepoFile("AGENTS.md");
        if (agents is null)
        {
            RepoSkipped.Add(claim);
            Skip(claim, "no repository beside this build — a document and the thing it documents ship with " +
                        "the source, and an installed copy has neither");
            return;
        }

        string root = Path.GetDirectoryName(agents)!;
        string[] missing = needs
            .Where(n => !File.Exists(Path.Combine(root, n)) && !Directory.Exists(Path.Combine(root, n)))
            .ToArray();
        if (!Check($"{claim} — its files are in the checkout", missing.Length == 0,
                   $"{string.Join(", ", missing)} — a checkout missing one of these is not an installed " +
                   "copy, so it is red rather than a skip (T251)"))
            return;

        assert(root);
    }

    /// <summary>The claims <see cref="Repo"/> stood down this run, so the allowance is derived from the
    /// skip that happened rather than remembered beside it.</summary>
    private static readonly List<string> RepoSkipped = new();

    /// <summary>Whether this is a run that had no business skipping the repository family — GitHub Actions
    /// sets <c>CI</c>, and the checkout is the working directory there.</summary>
    private static bool OnCi => Environment.GetEnvironmentVariable("CI") is { Length: > 0 };

    /// <summary>
    /// A file in the repository this build came out of, or null when there is no repository — an installed
    /// copy has none. Walks up from the binary and from the working directory, because <c>dotnet build</c>
    /// puts the exe four levels down while CI runs it with the checkout as the current directory.
    /// </summary>
    private static string? RepoFile(string relative)
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            DirectoryInfo? dir = new(start);
            while (dir is not null)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- Block F: what the note says

    /// <summary>
    /// T282. The count in front of the readings is a claim <em>about</em> the marks under it — nine of
    /// seventeen reach a field — and T278 left it as the one part of the read-out built inline and written
    /// straight to the console. Every check above it asks the classifier, which is the thing both the
    /// sentence and the marks were derived from, so a summary that recounted on its own line would have
    /// contradicted its own body with all of them passing.
    ///
    /// <para>So what is asserted here is the agreement, not the arithmetic: the numbers the sentence states
    /// are the lengths of the lists printed beneath it, and the block is a value both can be read off. The
    /// <c>--all</c> sentence is the same claim over the union of every profile's names and gets the same
    /// treatment, or the defect simply moves one screen down.</para>
    /// </summary>
    private static void Summary(List<ProbeEntry> log, string[] read, string[] unread)
    {
        HeaderReadership r = HeaderProbe.Readership(log);
        Check($"the readership counts every name on file ({read.Length + unread.Length})",
              r.Total == read.Length + unread.Length, $"{r.Total}");
        Check("read and unread account for all of them, with read derived rather than stored",
              r.Read + r.Unread.Count == r.Total && r.Read == read.Length, $"{r.Read} + {r.Unread.Count}");
        Check("and the unread list is the names nothing reads, not a count of them",
              unread.All(r.Unread.Contains) && r.Unread.Count == unread.Length,
              string.Join("; ", r.Unread));

        List<string> lines = ProbeCli.ReadershipLines(r);
        if (Check("the block leads with the sentence a reader sees first", lines.Count > 0
                  && lines[0].StartsWith("readership — "), lines.Count > 0 ? lines[0] : "<empty>"))
        {
            // The agreement itself: the sentence's own numbers, read back out of it, against the lines it
            // is a summary of. Anything that recounts on one side alone fails here and nowhere else.
            Check("its numbers are the lines beneath it, not a second count",
                  lines[0].Contains($"{r.Read} of {r.Total} ") && lines[0].Contains($"{r.Unread.Count} reach none")
                  && lines.Count(l => l.Contains(ProbeCli.UnreadMark)) == r.Unread.Count,
                  lines[0]);
            Check("every unread name is marked in the body it summarises",
                  unread.All(n => lines.Any(l => l.Contains(ProbeCli.UnreadMark) && l.Contains(n))));
            Check("and a name the parser reads that never arrived is named as the opposite fact",
                  r.NeverSent.All(n => lines.Any(l => l.Contains("read, never sent") && l.Contains(n))));
        }

        // A log carrying every name the parser reads and nothing else: no unread lines, and no prose
        // excusing them, or the block explains an absence it is not reporting.
        var allRead = new List<ProbeEntry>
        {
            new(Now, read.ToDictionary(n => n, _ => "0.5", StringComparer.OrdinalIgnoreCase)),
        };
        List<string> clean = ProbeCli.ReadershipLines(HeaderProbe.Readership(allRead));
        Check("a log with nothing unread says so and stops",
              clean.Count > 0 && clean[0].Contains("0 reach none")
              && !clean.Any(l => l.Contains(ProbeCli.UnreadMark)), string.Join(" | ", clean));
        Check("an empty log yields no block at all — not a heading over nothing",
              ProbeCli.ReadershipLines(HeaderProbe.Readership(new List<ProbeEntry>())).Count == 0);

        // The same sentence over the union of two profiles, which is what --all prints.
        List<HeaderSpread> spread = HeaderProbe.Spread(new List<(string, IReadOnlyList<ProbeEntry>)>
        {
            ("reads", allRead), ("does not", log),
        });
        string union = ProbeCli.SpreadReadership(spread);
        int unreadNames = spread.Count(h => !HeaderProbe.IsRead(h.Name));
        Check("the --all sentence counts the union of every profile's names",
              union.Contains($"{spread.Count - unreadNames} of these") && union.Contains($"{unreadNames} reach none"),
              union);
        Check("and it counts names, not readings — two profiles do not double the total",
              spread.Count == read.Length + unread.Length, $"{spread.Count}");
    }

    // ---------------------------------------------------------------- Block E: the cards' glyphs

    private static int Count(List<TailSample> seen) { lock (seen) return seen.Count; }

    /// <summary>Wait for a sweep to produce something. The tail is timer-driven (a watcher event, or
    /// the <see cref="TranscriptTail.SweepFloorMs"/> floor), so this is a deadline and not a sleep.</summary>
    private static bool Wait(Func<bool> until, int seconds = 12)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (until()) return true;
            Thread.Sleep(100);
        }
        return until();
    }

    // ---------------------------------------------------------------- synthetic inputs

    private static readonly double Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Nine local days of folded readings ending today, at a given hour coverage and spend —
    /// enough to cover any previous weekly window the ghost asks for.</summary>
    /// <param name="column">Write T287's over-quota column at all. Off by default, which is what a day
    /// folded before it existed looks like — the state the ghost must not read as "stayed inside".</param>
    /// <param name="overFrom">First hour of each day the account was past its included quota, or -1 for
    /// none. Inclusive, with <paramref name="overTo"/>, and only ever read when the column is written.</param>
    private static List<HourlyDay> FoldedWeek(double coverage, double perHour,
                                              bool column = false, int overFrom = -1, int overTo = -1)
    {
        var days = new List<HourlyDay>();
        for (int d = 8; d >= 0; d--)
        {
            DateTime date = DateTime.Today.AddDays(-d);
            var spend = new double[24];
            var count = new int[24];
            bool[]? over = column ? new bool[24] : null;
            for (int h = 0; h < 24; h++)
            {
                if (h >= 24 * coverage) continue;
                count[h] = 3;
                spend[h] = perHour;
                if (over is not null && overFrom >= 0 && h >= overFrom && h <= overTo) over[h] = true;
            }
            days.Add(new HourlyDay(date.Year * 10000 + date.Month * 100 + date.Day, spend, count, over));
        }
        return days;
    }

    /// <summary>
    /// Whole folded weeks, newest first: per week, how many hours of each day carry a reading and how
    /// many of those spent enough to count as active.
    ///
    /// <para>Four days per week rather than seven, at days 2–5 back from each week's start. The week a
    /// folded hour belongs to is <c>(now − then)/7</c>, so an hour at the very edge of a week can be
    /// pushed across it by the time of day the check happens to run — or by a DST change. Both ends are
    /// kept a full day clear of the boundary instead, which costs nothing here: the test is about how
    /// many hours a week holds, not about which day they fall on.</para>
    /// </summary>
    private static List<HourlyDay> FoldedWeeks((int covered, int active)[] weeks)
    {
        var days = new List<HourlyDay>();
        for (int w = 0; w < weeks.Length; w++)
            for (int d = 2; d <= 5; d++)
            {
                DateTime date = DateTime.Today.AddDays(-7 * w - d);
                var spend = new double[24];
                var count = new int[24];
                for (int h = 0; h < weeks[w].covered; h++)
                {
                    count[h] = 3;
                    spend[h] = h < weeks[w].active ? 0.004 : 0;
                }
                days.Add(new HourlyDay(date.Year * 10000 + date.Month * 100 + date.Day, spend, count));
            }
        return days;
    }

    /// <summary>One folded day with a single very heavy hour beside an ordinary one — the shape that
    /// would produce a "this hour is 4× heavy" bucket if nothing shrank or clamped it.</summary>
    private static List<HourlyDay> IntensityDay()
    {
        var spend = new double[24];
        var count = new int[24];
        for (int h = 9; h <= 13; h++) { count[h] = 4; spend[h] = 0.004; }
        spend[HeavyHour.Hour] = 0.20;          // ~50× an ordinary hour, before any guard
        spend[HeavyHour.Hour + 1] = 0.0015;    // active, but barely
        count[HeavyHour.Hour + 5] = 4;         // covered, never active: unknown, not light
        spend[HeavyHour.Hour + 5] = 0;
        DateTime d = HeavyHour.Date;
        return new List<HourlyDay> { new(d.Year * 10000 + d.Month * 100 + d.Day, spend, count) };
    }

    /// <summary>
    /// A folded store the fold never wrote — through the writer that writes the real one (T297).
    ///
    /// <para>This used to compose the line here: <c>d</c>, <c>s</c>, <c>c</c> appended into a
    /// <c>StringBuilder</c> of its own. The fixture was right and the writer was a copy, and a copy of a
    /// format drifts one way only — T287 added a column to <see cref="HourlyUsage.WriteAll"/> and every
    /// fixture kept writing three, so three checks went on asserting over days that silently meant
    /// "folded before the column existed". Nothing failed, which is the point.</para>
    ///
    /// <para>What a fixture still decides is <em>whether</em> a day carries the column, because absence is
    /// a reading here and one worth testing. That choice lives on <see cref="HourlyDay.Over"/>, which is
    /// the same field the real writer branches on — one format, one branch, two callers.</para>
    /// </summary>
    private static void WriteStore(List<HourlyDay> days)
    {
        var map = new Dictionary<int, HourlyDay>();
        foreach (HourlyDay d in days) map[d.Key] = d;
        HourlyUsage.WriteAll(ProfileKey, map, (long)Now);
    }

    // ---------------------------------------------------------------- plumbing

    private static double Unix(DateTime local) => new DateTimeOffset(local).ToUnixTimeSeconds();

    private static double Sum(double[] a) { double s = 0; foreach (double v in a) s += v; return s; }

    private static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    private static double Total(List<HourlyDay> days) { double s = 0; foreach (HourlyDay d in days) s += d.DaySpend; return s; }

    /// <summary>Run a body against a throwaway tree, removed whether it passed or threw.</summary>
    private static void Temp(Action<string> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "claude-tray-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        // Spelled the way the filesystem spells it rather than the way `%TEMP%` happens to (T169).
        root = LongPath(root);
        try { body(root); }
        catch (Exception e) { Fail("the section threw", e.Message); }
        finally { Remove(root); }
    }

    /// <summary>
    /// The same directory, segment by segment under the name the filesystem itself carries (T169).
    /// </summary>
    /// <remarks>
    /// A Windows CI runner's profile directory is an 8.3 alias — <c>RUNNER~1</c> — and the slug encoding
    /// maps that <c>~</c> to a <c>-</c> that matches no directory on the way down, so <see cref="Slug"/>'s
    /// two <c>TryProbe</c> assertions skipped on <em>every single run</em> there: the app's lossiest
    /// function, covered by nothing, reported as two green lines. Resolved here rather than documented,
    /// because a skip that fires every time is a check that does not exist.
    /// <para>What lets the alias through is that <see cref="Path.Combine(string, string)"/> is string
    /// concatenation: <c>%TEMP%</c> arrives already spelled <c>RUNNER~1</c> and nothing normalizes the
    /// result before it is handed to a section.</para>
    /// <para>Each segment is then looked up in its parent, because matching an 8.3 alias returns the entry
    /// under its real name. <b>Measured, against §XV.3's own premise:</b> .NET's path normalization
    /// <em>does</em> expand a short name — <c>Path.GetFullPath("C:\PROGRA~1")</c> answers
    /// <c>C:\Program Files</c> — so the design's "<c>FullName</c> does not expand 8.3" is not true on this
    /// runtime, and <c>GetFullPath</c> alone would have closed the skip. The walk stays because that
    /// expansion is an undocumented step conditional on the path containing a <c>~</c>, and because it
    /// leaves case alone; asking the filesystem depends on neither.</para>
    /// </remarks>
    private static string LongPath(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full) ?? "";
            if (root.Length == 0) return full;

            string rest = full[root.Length..].Trim('\\', '/');
            if (rest.Length == 0) return full;

            string built = root;
            foreach (string segment in rest.Split('\\', '/'))
            {
                FileSystemInfo? real = new DirectoryInfo(built).EnumerateFileSystemInfos(segment).FirstOrDefault();
                built = Path.Combine(built, real?.Name ?? segment);
            }
            return built;
        }
        catch { return path; }      // an unreadable ancestor is not this helper's problem to report
    }

    private static void Remove(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { Console.WriteLine($"  (could not remove {dir})"); }
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    private static bool Check(string name, bool ok, string detail = "")
    {
        if (ok) { _passed++; Console.WriteLine($"  ok    {name}"); }
        else Fail(name, detail);
        return ok;
    }

    private static void Near(string name, double actual, double expected, double tolerance)
    {
        double delta = Math.Abs(actual - expected);
        // Relative for anything but an exactness claim, so a tolerance means the same thing whether the
        // quantity is a fraction of a window or tokens per second.
        double allowed = tolerance * (tolerance > 0 ? Math.Max(1, Math.Abs(expected)) : 1);
        Check(name, delta <= allowed, $"{actual:0.##########} vs {expected:0.##########} (Δ {delta:0.###e+00})");
    }

    private static void Skip(string name, string why)
    {
        Skipped.Add((name, why));
        Console.WriteLine($"  skip  {name} — {why}");
    }

    private static void Fail(string name, string detail)
    {
        _failed++;
        string line = detail.Length > 0 ? $"{name}: {detail}" : name;
        Console.WriteLine("  FAIL  " + line);
        Failures.Add(line);
    }
}
