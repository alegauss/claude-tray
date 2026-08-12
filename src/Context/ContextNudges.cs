using System.Text.Json;

namespace ClaudeTray;

/// <summary>
/// The rate limiter behind the context nudge: at most one per project per week, remembered across
/// restarts in <c>%LocalAppData%\ClaudeTray\context-nudges.json</c>.
///
/// The cadence is the whole point. A threshold crossing is not an event that needs repeating — the
/// files are the developer&apos;s own, the number moves slowly, and a warning that reappears on every
/// launch stops being information and becomes noise. Once a week, per project, and only if the user
/// opted in at all.
/// </summary>
internal static class ContextNudges
{
    private const int CooldownDays = 7;

    /// <summary>Per profile (T125): a nudge is about one profile's context, and its once-a-week
    /// rate limit should not be spent by another profile's growth.</summary>
    private static string FilePath(string profileKey) =>
        ProfileStore.PathFor(profileKey, "context-nudges.json");

    /// <summary>Whether this project may be nudged about now.</summary>
    public static bool ShouldNotify(string profileKey, string slug, DateTime nowUtc)
    {
        Dictionary<string, long> seen = Load(profileKey);
        if (!seen.TryGetValue(slug, out long last)) return true;
        long now = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToUnixTimeSeconds();
        return now - last >= CooldownDays * 86400L;
    }

    /// <summary>Record that it was nudged, so the cooldown starts.</summary>
    public static void Mark(string profileKey, string slug, DateTime nowUtc)
    {
        // T239: an observing tray reads this store and adds nothing to it.
        if (ProfileStore.Observing) return;
        try
        {
            Dictionary<string, long> seen = Load(profileKey);
            seen[slug] = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToUnixTimeSeconds();
            StoreFile.CreateDirectory(Path.GetDirectoryName(FilePath(profileKey))!);
            StoreFile.WriteAllText(FilePath(profileKey), JsonSerializer.Serialize(seen));
        }
        catch { /* worst case the nudge repeats next week rather than never */ }
    }

    private static Dictionary<string, long> Load(string profileKey)
    {
        try
        {
            return File.Exists(FilePath(profileKey))
                ? JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(FilePath(profileKey)))
                  ?? new Dictionary<string, long>()
                : new Dictionary<string, long>();
        }
        catch { return new Dictionary<string, long>(); }
    }
}
