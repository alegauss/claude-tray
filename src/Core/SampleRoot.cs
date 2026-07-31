namespace ClaudeTray;

/// <summary>
/// Where a throwaway fixture is built. Prefers a location whose path does not contain the user's name,
/// because fixture screenshots get published — the whole point of a fixture is that nothing identifying
/// ends up in an image, and an absolute path spells out the Windows account name. Falls back to the temp
/// directory when the public folder isn't writable.
/// </summary>
internal static class SampleRoot
{
    public static string For(string name)
    {
        string? shared = Environment.GetEnvironmentVariable("PUBLIC");
        if (shared is { Length: > 0 } && Directory.Exists(shared))
        {
            string candidate = Path.Combine(shared, name);
            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch { /* not writable — fall through */ }
        }
        return Path.Combine(Path.GetTempPath(), name);
    }
}
