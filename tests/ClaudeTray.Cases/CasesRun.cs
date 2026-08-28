using Winwright.Processes;
using Winwright.Projects;
using Winwright.Scenarios;
using Winwright.Verdicts;
using Winwright.Windowing;

using Xunit;

namespace ClaudeTray.Cases;

/// <summary>
/// WW78. Every case in this repository, run.
/// <para>
/// What this replaces is a 3,004-line PowerShell harness whose every case function decided, for
/// itself, how long to wait for a read-back, whether to look again, how many goes an act gets, what a
/// missing read-back does to the exit code, and which line a failure points at. None of those is a
/// property of a case. All of them are properties of the run, and the engine owns every one — so what
/// is left here is naming the files, measuring what only this repository can measure, and reading the
/// verdict.
/// </para>
/// <para>
/// One test and not one per case, deliberately. The engine runs the selection and answers a verdict
/// over all of it, including which cases it left alone and why; splitting that into one xUnit fact
/// per case would relaunch the application per case and throw away the window lending the fixtures
/// declare.
/// </para>
/// </summary>
public sealed class CasesRun
{
    /// <summary>
    /// WW79. The one precondition no engine could measure, because it is about this application's
    /// data rather than about the desk.
    /// <para>
    /// The Statistics page's panes are Collapsed — and therefore absent from the accessibility tree —
    /// until a report renders, and a report is rendered from this machine's Claude Code transcripts.
    /// On a machine with none there is no pane body to look for, so a case asserting one would be a
    /// red about an empty folder: the exact inversion this whole tool exists to refuse.
    /// </para>
    /// <para>
    /// The path is spelled here and also in <c>src/Usage/ActivityProfile.cs</c>, which is a
    /// duplication rather than a shared constant on purpose: the alternative is this project
    /// referencing the WPF application it drives, and the application under test must not be on the
    /// driving project's reference graph. One path, named once, with this sentence beside it.
    /// </para>
    /// </summary>
    private const string Requires = "a transcript to report on";

    /// <summary>
    /// WW81. The second one, and it is about this machine's accounts rather than its transcripts.
    /// <para>
    /// The profile card is Collapsed below two profiles, so on a one-profile machine — which is most
    /// machines and every hosted runner — there is no picker in the tree at all. A round trip
    /// asserting one would be a red about how many Claude Code accounts somebody happens to have.
    /// </para>
    /// <para>
    /// Measured by asking the application, not by counting config directories here. Discovery is real
    /// logic — registered dirs, a settings store, deduped labels — and a second implementation of it
    /// in this file would be a second answer that drifts. The script did the same thing for the same
    /// reason, and the count is the first line of the application's own read-out.
    /// </para>
    /// </summary>
    private const string RequiresProfiles = "a second profile";

    [Fact]
    public void Every_case_in_this_repository_runs()
    {
        var desk = Desk.Read();
        if (!desk.CanObserve)
        {
            // Named rather than skipped silently. A run that could observe nothing is a third
            // verdict, and a test framework with no word for it gets the closest honest thing: a
            // pass that says out loud it checked nothing.
            Assert.True(true, $"nothing ran: this desk lacks {desk.FirstAbsent!.Name}");
            return;
        }

        var project = ProjectDeclaration.Find(Repository());
        var declared = ScenarioFile.Across(ScenarioFile.LoadAll(Path.Combine(Repository(), "cases")));

        using var register = ProcessRegister.For(project);
        var verdict = Suite.Launch(declared, Selection.All, register, project, measured: Measured());

        // The whole reading, not just the outcome: a filtered run qualifies its pass before it states
        // it, and what the run did not do is part of what it concluded.
        //
        // The verdict's whole report is the failure message, and it is here because of what its
        // absence cost twice over. The first red said `Expected: Passed / Actual: Broken` and nothing
        // else; the second said the sentence alone — `Broken: all 3 cases, 4 assertions` — which
        // names no case. `Render()` is the line per case, and a reader needs the line, not the count.
        // A red step carries its diagnosis is this project's rule about steps, and it is no different
        // one layer up.
        Assert.True(verdict.Outcome == RunOutcome.Passed, Read(verdict));
        Assert.Equal(0, verdict.ExitCode);

        // And nothing was left on the desk. This is the finding the old harness produced by having
        // its own finally block in every case function.
        Assert.Empty(register.StopAll());
    }

    /// <summary>
    /// The whole reading, down to the assertion that went red.
    /// <para>
    /// It took three reds to get here, and each one hid the next. <c>Assert.Equal(Passed, Outcome)</c>
    /// said only <em>Broken</em>. The verdict's sentence added a count. <see cref="SuiteVerdict.Render" />
    /// added a line per case — and a line per case names the case and not what it read, so a reader
    /// still had to attach a debugger to find out which assertion failed and against what.
    /// </para>
    /// <para>
    /// xUnit shows one message, so the message has to be the report. This is the same rule the engine
    /// applies to a step — a red carries its diagnosis — applied to the runner that reports the steps,
    /// and it is the runner's job because the engine already had every one of these values.
    /// </para>
    /// </summary>
    private static string Read(SuiteVerdict verdict)
    {
        var lines = new List<string>(verdict.Render());

        foreach (var unhappy in verdict.Unhappy)
        {
            lines.Add($"");
            lines.Add($"{unhappy.Declared.Name}:");

            // Failures, then holes, then harness errors, because that is the order a reader needs
            // them in: what went wrong, what could not be asked, and what broke before either.
            lines.AddRange(unhappy.Verdict.Failures.Select(one => $"  failed    {one}"));
            lines.AddRange(unhappy.Verdict.Unchecked.Select(one => $"  unchecked {one}"));
            // The stack too, and only for a harness error. A HarnessError is the engine saying it
            // threw rather than the application failing, so its own frames are the whole of what a
            // reader needs — and its ToString drops them, which cost a round trip the one time it
            // happened.
            lines.AddRange(unhappy.Verdict.Broke.Select(
                one => $"  broke     {one}{(one.StackTrace is null ? "" : Environment.NewLine + one.StackTrace)}"));

            // And the trace, because a case can fail at a step that is not where it went wrong. WW243
            // is that exactly: a navigation carries no check, so an act of it that never landed is
            // reported by nothing — and the red arrives one step later, naming the locator of the
            // step that was right to find nothing.
            lines.AddRange(unhappy.Trace.Select(one => $"  trace     {one}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// What this machine turned out to have, measured once for the whole run. A name a case requires
    /// and nothing measures is refused at load rather than run as a green, so adding a
    /// <c>needs</c> to a case means adding its measurement here.
    /// </summary>
    private static PreconditionSet Measured()
    {
        var projects = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

        // Any one transcript is enough: the report renders from whatever it finds, and a machine with
        // one session renders a report with small numbers in it rather than no report.
        var transcripts = Directory.Exists(projects)
            && Directory.EnumerateFiles(projects, "*.jsonl", SearchOption.AllDirectories).Any();

        var (profiles, because) = Profiles();

        return PreconditionSet.Of(
            transcripts
                ? Precondition.Met(Requires)
                : Precondition.Absent(Requires, $"no *.jsonl under {projects}, so no report can render"),
            profiles >= 2
                ? Precondition.Met(RequiresProfiles)
                : Precondition.Absent(RequiresProfiles, because));
    }

    /// <summary>
    /// How many Claude Code profiles this machine exposes, read off the application's own
    /// <c>--profiles</c> read-out, and why the answer is not usable where it is not.
    /// <para>
    /// WW81. A count and never a list: what a case needs to know is whether the picker is in the tree
    /// at all, and the names are this desk's accounts. The first line is
    /// <c>Claude Code profiles: N</c>, so nothing here parses the entries under it.
    /// </para>
    /// <para>
    /// A launch that fails is an absence and never a zero. "The application would not answer" and
    /// "this machine has one profile" are different facts, and a run that reported the first as the
    /// second would say the picker is missing when nobody looked.
    /// </para>
    /// </summary>
    private static (int Count, string Because) Profiles()
    {
        var project = ProjectDeclaration.Find(Repository());
        var asking = new System.Diagnostics.ProcessStartInfo(project.Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        asking.ArgumentList.Add("--profiles");

        try
        {
            using var running = System.Diagnostics.Process.Start(asking);
            if (running is null)
                return (0, $"{project.Executable} --profiles started nothing, so the count was never taken");

            var said = running.StandardOutput.ReadToEnd();
            running.WaitForExit(30_000);

            var counted = System.Text.RegularExpressions.Regex.Match(
                said, @"Claude Code profiles:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(1));

            if (!counted.Success)
                return (0, $"--profiles printed no count this could read: {Trimmed(said)}");

            var count = int.Parse(counted.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return count >= 2
                ? (count, "")
                : (count, $"--profiles reports {count}, and the profile card is Collapsed below two");
        }
        catch (Exception refused)
            when (refused is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (0, $"{project.Executable} --profiles could not be run: {refused.Message}");
        }
    }

    /// <summary>The first line of what a process said, for an absence that has to fit on one.</summary>
    /// <param name="said">Everything it printed.</param>
    private static string Trimmed(string said) =>
        said.Split('\n', StringSplitOptions.RemoveEmptyEntries) is [var first, ..] ? first.Trim() : "nothing at all";

    /// <summary>This repository's root, found by walking up to the file the project declares itself in.</summary>
    private static string Repository()
    {
        var walking = new DirectoryInfo(AppContext.BaseDirectory);
        while (walking is not null && !File.Exists(Path.Combine(walking.FullName, ProjectDeclaration.FileName)))
            walking = walking.Parent;

        Assert.NotNull(walking);
        return walking.FullName;
    }
}
