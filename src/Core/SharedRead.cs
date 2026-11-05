namespace ClaudeTray;

/// <summary>
/// Reading a file somebody may be writing right now (T419).
///
/// <para><b>Why not <c>File.ReadLines</c>.</b> It opens with <c>FileShare.Read</c>, which refuses a
/// concurrent writer: on .NET 10 the open throws <c>IOException</c> while any write handle is held,
/// even one that itself shares <c>ReadWrite | Delete</c>. Claude Code holds exactly such a handle on
/// the transcript of the conversation running now, so every reader that went through
/// <c>File.ReadLines</c> saw that conversation as empty — and the two that cache per file kept the
/// empty answer. The same share mode made the app's own append-only stores block their own reader
/// (<c>UsageHistory.Load</c> against a poll's <c>Append</c>).</para>
///
/// <para>Sharing <c>ReadWrite | Delete</c> is what <see cref="TranscriptTail"/> has done since T97, and
/// a reader that tolerates a writer can see a last line cut mid-append: every caller here already
/// parses line by line and skips what does not parse, so a torn tail costs one line, once.</para>
///
/// <para>The open is <b>eager</b>, as <c>File.ReadLines</c>' is: a missing or locked file throws at
/// the call, so a <c>try</c> around the call still catches it.</para>
/// </summary>
internal static class SharedRead
{
    public static IEnumerable<string> Lines(string path)
    {
        var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan));
        return Each(reader);
    }

    private static IEnumerable<string> Each(StreamReader reader)
    {
        using (reader)
        {
            string? line;
            while ((line = reader.ReadLine()) != null) yield return line;
        }
    }
}
