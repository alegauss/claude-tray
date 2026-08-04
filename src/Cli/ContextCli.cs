namespace ClaudeTray;

/// <summary>The `--context` family: the headless context report and everything it prints. Split out of `Program.cs` by T132 —
/// moved verbatim.</summary>
internal static class ContextCli
{
    // Headless report for the context scanner — the CLI half of the Context Load Inspector, the same
    // role `--insights` plays for UsageInsights: the whole model is validated here before any XAML.
    // Flags: a project slug or directory name to drill into one project, `--all` for every project,
    // `--calibrate` for the estimate-vs-measured fit, `--no-cache` to force a cold scan.
    internal static void PrintContext(string[] flags)
    {
        bool all = flags.Contains("--all");
        bool calibrate = flags.Contains("--calibrate");
        bool noCache = flags.Contains("--no-cache");
        string? filter = flags.FirstOrDefault(f => !f.StartsWith("--"));

        // The report is full of "≈" — without this the console renders every estimate as a
        // replacement character on a non-UTF-8 codepage (cmd.exe defaults to 850 here).
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected output */ }
        // A developer report, read alongside the source: invariant numbers throughout, so
        // "2.79 chars/token" can't render as "2,79" and read like a thousands separator. Safe to set
        // process-wide here — this command prints and exits, it never reaches the localized UI.
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        // `--root <dir>` points the scan at a stand-in for ~/.claude. It exists so the scanner can be
        // exercised against a fixture tree (imports, cycles, orphans, a zero-state project) instead
        // of only against whatever the dev machine happens to contain.
        int rootAt = Array.IndexOf(flags, "--root");
        string? root = rootAt >= 0 && rootAt + 1 < flags.Length ? flags[rootAt + 1] : null;
        if (root != null) filter = flags.FirstOrDefault(f => !f.StartsWith("--") && f != root);

        // `--sample` builds a throwaway ~/.claude lookalike and scans that instead: every rule fires
        // on demand, and a screenshot taken from it carries no real project names.
        if (flags.Contains("--sample"))
        {
            try
            {
                root = ContextFixture.Build(DateTimeOffset.UtcNow.UtcDateTime);
                Console.WriteLine("sample fixture: " + root);
                Console.WriteLine();
            }
            catch (Exception e)
            {
                Console.WriteLine("error building the sample fixture: " + e.Message);
                return;
            }
        }

        var scan = ContextScanner.Scan(DateTimeOffset.UtcNow.UtcDateTime, new ContextScanner.Options
        {
            UseCache = !noCache && root == null,   // a fixture scan must never poison the real cache
            ClaudeRoot = root ?? ContextScanner.DefaultClaudeRoot,
        });
        if (ReadOut.Failed(scan.Error)) return;

        // "walked", not "files": the report's summary counts what the scan KEPT, and the two differ by the
        // ones the walk visited and discarded — 1021 against 881 on the machine that produced T268. One
        // vocabulary across both artifacts is the whole of that fix.
        Console.WriteLine($"Context load — {scan.Projects.Count} projects, {scan.FilesWalked} files walked, " +
                          $"{scan.ElapsedMs:0}ms {(scan.FromCache ? "(cached)" : "(fresh scan)")}");
        if (scan.Truncated)
            Console.WriteLine("  ! the walk hit its file/directory cap — totals below are a floor, not a total");
        Console.WriteLine();

        // `--usage` is the evidence: which skills and agents were actually invoked, mined from the
        // transcripts. The one number that turns "trim your skills" into a decision.
        if (flags.Contains("--usage"))
        {
            PrintUsage(scan, root);
            return;
        }

        // `--prompt` prints the same cleanup prompt the window copies to the clipboard, so what gets
        // handed to Claude is inspectable (and pipeable) rather than only visible in a paste.
        if (flags.Contains("--prompt"))
        {
            PrintPrompt(scan, root, filter);
            return;
        }

        // `--check` is the advisor rather than the measurement: findings grouped by severity, each
        // with the concrete fix. It replaces the table because the point is what to do, not what is.
        if (flags.Contains("--check"))
        {
            PrintFindings(scan, root);
            return;
        }

        // Shared sources first: these load for every project, so they belong to no project and to
        // all of them. Counting them once here is what keeps a cross-project total honest.
        Console.WriteLine($"Shared (every session, every project) — eager {TokenEstimate.Format(scan.SharedEagerTokens)}:");
        // The 31 plugin skills are always folded here: they are the same list for every project, so
        // reading them one by one belongs to a skills view, not to a project's breakdown.
        PrintSources(scan.Shared, groupSkills: !flags.Contains("--skills"));
        Console.WriteLine();

        if (filter != null)
        {
            ContextProject? p = scan.Projects.FirstOrDefault(x =>
                x.Slug.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                x.Name.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                x.ShortPath.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                x.Path.Equals(filter, StringComparison.OrdinalIgnoreCase));
            if (p == null) { Console.WriteLine($"no project matching '{filter}'"); return; }
            PrintProjectDetail(scan, p);
            return;
        }

        // The summary table: one line per project, sorted by what it costs every session.
        Console.WriteLine($"{"project",-34} {"eager",8} {"observed",9} {"delta",8} {"src",4} {"mem",4}  state");
        foreach (ContextProject p in scan.Projects)
        {
            int est = scan.EstimatedSessionZero(p);
            string observed = p.Observed is { } o ? TokenEstimate.Format(o.Median) : "—";
            string delta = p.Observed is { } o2 ? $"{o2.Median - est:+#;-#;0}" : "—";
            int memory = p.Sources.Count(s => s.Kind is ContextKind.MemoryFile or ContextKind.MemoryIndex);
            Console.WriteLine($"{Clip(p.ShortPath, 34),-34} {TokenEstimate.Format(est),8} {observed,9} " +
                              $"{delta,8} {p.Sources.Count,4} {memory,4}  {StateWord(p)}{(p.Truncated ? " !capped" : "")}");
        }

        Console.WriteLine();
        long bytes = scan.Shared.Sum(s => s.Bytes) + scan.Projects.Sum(p => p.Bytes);
        Console.WriteLine($"footprint: {Kb(bytes)} across {scan.Projects.Sum(p => p.Sources.Count) + scan.Shared.Count} files");
        Console.WriteLine($"heaviest eager: {string.Join(", ", scan.Projects.Take(3).Select(p => $"{p.ShortPath} {TokenEstimate.Format(scan.EstimatedSessionZero(p))}"))}");
        int orphans = scan.Projects.Count(p => p.State != PathState.Resolved);
        if (orphans > 0) Console.WriteLine($"unresolved project dirs: {orphans} (see 'state' column)");

        if (all)
            foreach (ContextProject p in scan.Projects)
            {
                Console.WriteLine();
                PrintProjectDetail(scan, p);
            }

        if (calibrate) PrintCalibration(scan);
    }

    private static void PrintProjectDetail(ContextScan scan, ContextProject p)
    {
        int est = scan.EstimatedSessionZero(p);
        Console.WriteLine($"=== {p.Slug}");
        Console.WriteLine($"    path: {(p.Path.Length > 0 ? p.Path : "(not a filesystem path)")}" +
                          $"  [{StateWord(p)}{(p.FromTranscript ? ", from transcript cwd" : "")}" +
                          $"{(p.Truncated ? ", nested walk capped" : "")}]");
        Console.WriteLine($"    session zero (estimated): {TokenEstimate.Format(est)} " +
                          $"= shared {TokenEstimate.Format(scan.SharedEagerTokens)} + project {TokenEstimate.Format(p.EagerTokens)}");
        if (p.Observed is { } o)
            Console.WriteLine($"    session zero (observed):  {TokenEstimate.Format(o.Median)} median of {o.Samples} " +
                              $"session{(o.Samples == 1 ? "" : "s")} ({TokenEstimate.Format(o.Min)}–{TokenEstimate.Format(o.Max)}), " +
                              $"{o.Model}, ≈${o.Cost:0.000} per session start");
        else
            Console.WriteLine($"    session zero (observed):  no fresh session in the window " +
                              $"({p.TranscriptsRead} transcript{(p.TranscriptsRead == 1 ? "" : "s")} checked)");
        PrintSources(p.Sources, groupSkills: false);
    }

    // One line per source: kind, eager/lazy, bytes, estimated tokens, the eager share of those
    // tokens, last-modified, label. Grouped by kind so the eager block reads first.
    private static void PrintSources(List<ContextSource> sources, bool groupSkills)
    {
        if (sources.Count == 0) { Console.WriteLine("    (none)"); return; }

        foreach (var group in sources
                     .GroupBy(s => s.Kind)
                     .OrderBy(g => (int)g.Key))
        {
            // The 31 plugin skills would drown the summary; fold them into one line unless asked.
            if (groupSkills && group.Key is ContextKind.Skill or ContextKind.Agent && group.Count() > 4)
            {
                Console.WriteLine($"    {group.Key,-20} {"index",-9} {Kb(group.Sum(s => s.Bytes)),9} " +
                                  $"{TokenEstimate.Format(group.Sum(s => s.Tokens)),8} " +
                                  $"{TokenEstimate.Format(group.Sum(s => s.EagerTokens)),8}  " +
                                  $"{group.Count()} entries (bodies lazy, descriptions eager)");
                continue;
            }

            foreach (ContextSource s in group.OrderByDescending(s => s.EagerTokens).ThenByDescending(s => s.Bytes))
                Console.WriteLine($"    {s.Kind,-20} {Mode(s),-9} {Kb(s.Bytes),9} " +
                                  $"{TokenEstimate.Format(s.Tokens),8} {TokenEstimate.Format(s.EagerTokens),8}  " +
                                  $"{s.ModifiedUtc.ToLocalTime():yyyy-MM-dd}  {Clip(s.Label, 44)}" +
                                  $"{(s.Note != null ? "  ! " + s.Note : "")}");
        }
    }

    // `--context-report <file.md>`: scan, evaluate, mine the evidence, and write it all out as one
    // markdown document. Everything it prints about itself (path, size) is so the caller knows what it
    // got without opening the file.
    internal static void WriteContextReport(string path)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected output */ }
        DateTime now = DateTimeOffset.UtcNow.UtcDateTime;

        ContextScan scan = ContextScanner.Scan(now);
        if (ReadOut.Failed(scan.Error)) return;

        UsageEvidence evidence = ContextUsage.Compute(now);
        List<Finding> findings = ContextRules.Evaluate(scan, now, evidence);
        var debt = scan.Projects.ToDictionary(p => p.Slug, p => ContextRules.Debt(scan, p, findings));
        int baseTokens = ContextScanner.Calibrate(scan) is { Base: > 0 } c
            ? (int)Math.Round(c.Base)
            : ContextScanner.FallbackBaseTokens;

        string markdown = ContextReport.Build(scan, findings, evidence, debt, baseTokens,
            DateTimeOffset.Now.LocalDateTime);
        try
        {
            string full = Path.GetFullPath(path);
            if (Path.GetDirectoryName(full) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            File.WriteAllText(full, markdown);
            // Invariant, like the rest of the developer CLI: "18.9 KB" must not render as "18,9 KB".
            Console.WriteLine(FormattableString.Invariant(
                $"wrote {full} ({markdown.Length / 1024.0:0.#} KB, {findings.Count} findings)"));
        }
        catch (Exception e)
        {
            Console.WriteLine("error: " + e.Message);
        }
    }

    // The advisor's headless half (`--context --check`): every finding from ContextRules, grouped by
    // severity, each on two lines — what is true, then what to do about it. Verified against a whole
    // machine here before any of it reaches the window.
    private static void PrintFindings(ContextScan scan, string? root)
    {
        DateTime now = DateTimeOffset.UtcNow.UtcDateTime;
        // The evidence pass is what lets a "never invoked" finding exist at all; without it the rule
        // simply doesn't fire, which is the right failure mode.
        UsageEvidence evidence = ContextUsage.Compute(now, new ContextUsage.Options
        {
            ClaudeRoot = root ?? ContextScanner.DefaultClaudeRoot,
            UseCache = root == null,
        });
        Console.WriteLine($"usage evidence: {ContextUsage.Summary(evidence)}");
        Console.WriteLine();

        List<Finding> findings = ContextRules.Evaluate(scan, now, evidence);
        if (findings.Count == 0)
        {
            Console.WriteLine("no findings — nothing to advise on");
            return;
        }

        Console.WriteLine($"{findings.Count} findings: " + string.Join(", ",
            Enum.GetValues<RuleSeverity>()
                .Select(s => (severity: s, count: findings.Count(f => f.Severity == s)))
                .Where(x => x.count > 0)
                .Select(x => $"{x.count} {x.severity.ToString().ToLowerInvariant()}")));

        foreach (var group in findings.GroupBy(f => f.Severity).OrderBy(g => (int)g.Key))
        {
            Console.WriteLine();
            Console.WriteLine($"--- {group.Key.ToString().ToUpperInvariant()} ({group.Count()})");
            foreach (Finding f in group)
            {
                // Unclipped: a duplication finding's scope is every project in the cluster, and
                // Clip keeps the tail — it would hide the first project's name.
                Console.WriteLine($"  [{f.RuleId}] {f.Scope}");
                Console.WriteLine($"      {f.Message}");
                Console.WriteLine($"      fix: {f.Fix}");
            }
        }
    }

    // The cleanup prompt (`--context --prompt [project]`): exactly what the window's "Copy cleanup
    // prompt" button puts on the clipboard. Without a project name it is the machine-wide one.
    private static void PrintPrompt(ContextScan scan, string? root, string? filter)
    {
        DateTime now = DateTimeOffset.UtcNow.UtcDateTime;
        UsageEvidence evidence = ContextUsage.Compute(now, new ContextUsage.Options
        {
            ClaudeRoot = root ?? ContextScanner.DefaultClaudeRoot,
            UseCache = root == null,
        });
        List<Finding> all = ContextRules.Evaluate(scan, now, evidence);

        ContextProject? project = filter is { Length: > 0 }
            ? scan.Projects.FirstOrDefault(x =>
                x.Slug.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                x.Name.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                x.ShortPath.Equals(filter, StringComparison.OrdinalIgnoreCase))
            : null;

        if (filter is { Length: > 0 } && project == null)
        {
            Console.WriteLine($"no project matching '{filter}'");
            return;
        }

        List<Finding> findings = project is null
            ? all.Where(f => f.Scope == "~/.claude" || f.RuleId is "memory-duplicated"
                or "project-dir-dead" or "project-dir-dead-empty").ToList()
            : all.Where(f => f.Scope == project.ShortPath).ToList();

        string title = project is null
            ? ContextScanner.DefaultClaudeRoot
            : project.Path.Length > 0 ? project.Path : project.ShortPath;

        // The truncation goes INTO the text, not just into the header above it (T267): what a person
        // copies starts at the `#`, and this is read by something that will act on the list.
        Console.WriteLine(ContextPrompt.Build(title, findings, Array.Empty<ContextSource>(), 0,
                                              truncated: scan.Truncated));
    }

    // The evidence report (`--context --usage`): every skill and agent the scan found, with how often
    // it was actually invoked over the last 90 days. Sorted so the expensive-and-unused come first —
    // that is the actionable end of the list.
    private static void PrintUsage(ContextScan scan, string? root)
    {
        DateTime now = DateTimeOffset.UtcNow.UtcDateTime;
        UsageEvidence evidence = ContextUsage.Compute(now, new ContextUsage.Options
        {
            ClaudeRoot = root ?? ContextScanner.DefaultClaudeRoot,
            UseCache = root == null,
        });

        Console.WriteLine($"Evidence of use over {evidence.WindowDays} days — {ContextUsage.Summary(evidence)}");
        if (evidence.Error != null) Console.WriteLine("  ! " + evidence.Error);
        Console.WriteLine("  memory files carry no usage annotation on purpose: a recall leaves no");
        Console.WriteLine("  structured trace, and only message content would show it (never read).");
        Console.WriteLine();

        // One row per distinct skill/agent. The same skill can be visible from several projects, so
        // they are folded by label — the eager cost is paid once per session either way.
        var entries = scan.Shared.Concat(scan.Projects.SelectMany(p => p.Sources))
            .Where(s => s.Kind is ContextKind.Skill or ContextKind.Agent)
            .GroupBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(s => (source: s, stat: evidence.For(s), unused: evidence.CanReportZero(s)))
            .OrderBy(x => x.stat?.Total ?? 0)
            .ThenByDescending(x => x.source.EagerTokens)
            .ToList();

        Console.WriteLine($"{"skill / agent",-42} {"used 90d",9} {"30d",5} {"eager",7}  last used");
        foreach (var (source, stat, unused) in entries)
        {
            string used = stat is { Total: > 0 }
                ? stat.Total.ToString()
                : unused ? "never" : "—";
            string last = stat is { Total: > 0 } ? stat.LastUsedUtc.ToLocalTime().ToString("yyyy-MM-dd") : "";
            Console.WriteLine($"{Clip(source.Label, 42),-42} {used,9} {stat?.Recent ?? 0,5} " +
                              $"{TokenEstimate.Format(source.EagerTokens),7}  {last}");
        }

        int neverTokens = entries.Where(x => x.unused && (x.stat?.Total ?? 0) == 0)
            .Sum(x => x.source.EagerTokens);
        int neverCount = entries.Count(x => x.unused && (x.stat?.Total ?? 0) == 0);
        Console.WriteLine();
        if (neverCount > 0)
            Console.WriteLine($"{neverCount} never invoked in the last {evidence.WindowDays} days of transcripts, " +
                              $"costing {TokenEstimate.Format(neverTokens)} of eager index in every session");
        else
            Console.WriteLine("nothing is provably unused");
    }

    // The estimate-vs-measurement fit: proves the token numbers are a measurement with a correction,
    // not a guess. `Base` is what no filesystem scan can see (system prompt + tool definitions).
    private static void PrintCalibration(ContextScan scan)
    {
        Console.WriteLine();
        if (ContextScanner.Calibrate(scan) is not { } c)
        {
            Console.WriteLine("calibration: not enough projects with observed sessions (need 3+)");
            return;
        }

        Console.WriteLine($"calibration over {c.Points} projects with observed sessions:");
        Console.WriteLine($"  base overhead, invisible to any filesystem scan (system prompt, tool");
        Console.WriteLine($"  definitions, MCP schemas): {TokenEstimate.Format((int)c.Base)} median " +
                          $"(p25 {TokenEstimate.Format((int)c.BaseP25)}, p75 {TokenEstimate.Format((int)c.BaseP75)})");
        Console.WriteLine($"  measured chars/token, from {c.SlopePairs} project pairs: {c.CharsPerToken:0.00}  " +
                          $"(estimator uses {TokenEstimate.ProseCharsPerToken:0.00} prose / " +
                          $"{TokenEstimate.CodeCharsPerToken:0.00} code / {TokenEstimate.TableCharsPerToken:0.00} table)");
        Console.WriteLine($"  corrected estimate (scan + base) vs observed: within ±{ContextScanner.Calibration.BandPct:0}% " +
                          $"for {c.WithinBand}/{c.Points} projects, median error {c.MedianErrorPct:0.#}%, worst {c.WorstErrorPct:0.#}%");
        Console.WriteLine();
        Console.WriteLine($"  {"project",-34} {"chars",9} {"scanned",9} {"corrected",10} {"observed",9} {"err",7}");
        foreach (var p in c.Samples)
            Console.WriteLine($"  {Clip(p.Slug, 34),-34} {p.Chars,9:0} {TokenEstimate.Format(p.Estimated),9} " +
                              $"{TokenEstimate.Format(p.Corrected(c.Base)),10} {TokenEstimate.Format(p.Observed),9} " +
                              $"{p.ErrorPct(c.Base),6:0.#}%");
    }

    private static string Mode(ContextSource s) => s.Mode switch
    {
        LoadMode.Eager => "eager",
        // A skill/agent body is lazy, but its description is in the always-loaded index — the
        // "index" word marks exactly that, rather than pretending the whole file is free.
        LoadMode.Lazy => s.EagerTokens > 0 ? "index" : "lazy",
        _ => "not-loaded",
    };

    private static string StateWord(ContextProject p) => p.State switch
    {
        PathState.Resolved => "ok",
        PathState.Missing => "MISSING",
        _ => "not-a-path",
    };

    private static string Kb(long bytes) => bytes < 1024
        ? $"{bytes} B"
        : (bytes / 1024.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KB";

    private static string Clip(string s, int max) => s.Length <= max ? s : "…" + s[^(max - 1)..];
}
