namespace ClaudeTray;

/// <summary>
/// Creating a file some flag was told to write — directory and all.
///
/// <para><b>Why this exists (T187).</b> Every capture flag takes an output path, and the three
/// <c>SaveSnapshot</c> implementations behind them called <see cref="File.Create(string)"/> on it
/// directly, with nothing creating the directory above. Capturing into a folder that did not exist yet
/// threw <c>DirectoryNotFoundException</c> from a <c>DispatcherTimer</c> tick — an unhandled exception
/// with a WPF stack, raised <em>after</em> the window had rendered and the expensive part was done. What a
/// person is doing when they hit that is verifying a change, and a stack trace in the middle of it reads
/// as "the change broke the app" rather than "the folder was new"; the cost is a wrong diagnosis on the
/// one surface where somebody is already suspicious.</para>
///
/// <para>Every sibling already did the opposite — <c>PreviewCli.RenderTest</c> opens with
/// <c>Directory.CreateDirectory</c>, <c>UsageHistory.Append</c> creates its own parent on every write — so
/// this is the family's rule, stated once instead of at each call site.</para>
/// </summary>
internal static class OutFile
{
    /// <summary>Create (truncating any existing file) the file at <paramref name="path"/>, creating the
    /// directory above it first. Relative paths are resolved against the current directory before the
    /// parent is taken, so <c>out.png</c> — whose <c>GetDirectoryName</c> is empty — is not mistaken for a
    /// path with no parent to create.</summary>
    internal static FileStream Create(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);
        return File.Create(path);
    }
}
