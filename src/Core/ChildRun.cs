using System.Diagnostics;

namespace ClaudeTray;

/// <summary>
/// Running a child process to completion within a bound, the way every caller here needs it (T426).
///
/// <para><b>Two mistakes this exists to make impossible.</b> Reading a redirected stream to its end
/// <em>before</em> the timed wait means the bound is reached only after the child has already exited, so
/// a wedged <c>wsl.exe</c> froze the tray menu that asked for it. And a redirected stream nobody reads
/// fills its pipe buffer: a chatty child then blocks on its own write and times out a check that was
/// healthy. So both streams are drained alongside the wait, and the bound is the wait's.</para>
///
/// <para>On timeout the <b>whole tree</b> is killed — <c>cmd /c claude</c> is cmd plus a Node process, and
/// disposing a <see cref="Process"/> stops neither.</para>
/// </summary>
internal static class ChildRun
{
    /// <summary>What a run came to. <see cref="ExitCode"/> is null when it timed out or never started.</summary>
    internal readonly record struct Result(int? ExitCode, string Output, string Error, bool TimedOut);

    public static Result Run(ProcessStartInfo psi, TimeSpan timeout)
        => RunAsync(psi, timeout).GetAwaiter().GetResult();

    public static async Task<Result> RunAsync(ProcessStartInfo psi, TimeSpan timeout)
    {
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        using Process? p = Process.Start(psi);
        if (p is null) return new Result(null, "", "could not start " + psi.FileName, false);

        Task<string> output = p.StandardOutput.ReadToEndAsync();
        Task<string> error = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* it finished between the two */ }
            // The pipes close with the tree; what was written before the kill is still worth keeping.
            return new Result(null, await Drained(output).ConfigureAwait(false),
                              await Drained(error).ConfigureAwait(false), true);
        }
        return new Result(p.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false), false);
    }

    private static async Task<string> Drained(Task<string> read)
    {
        try { return await read.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch { return ""; }
    }
}
