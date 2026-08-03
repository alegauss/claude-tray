using System.Text.Json;

namespace ClaudeTray;

/// <summary>One captured reading: when it was taken, and the rate-limit headers exactly as sent.</summary>
internal readonly record struct ProbeEntry(double T, IReadOnlyDictionary<string, string> Headers)
{
    public string? Get(string name) => Headers.TryGetValue(name, out string? v) ? v : null;
}

/// <summary>One value a categorical header has taken, and the span over which it was seen.</summary>
internal readonly record struct VocabValue(string Value, int Count, double First, double Last);

/// <summary>Every value one categorical header has taken across a log, oldest first sighting first.</summary>
internal readonly record struct HeaderVocab(string Name, IReadOnlyList<VocabValue> Values)
{
    /// <summary>More than one value on file — the header has been seen to move, so its vocabulary is
    /// measured rather than assumed from a single sample.</summary>
    public bool Moved => Values.Count > 1;
}

/// <summary>How many of a log's readings carried one header name, out of how many there were.</summary>
internal readonly record struct HeaderPresence(string Name, int Readings, int Total)
{
    /// <summary>Sent on some readings and not others — which is a reading in itself, and the one thing a
    /// vocabulary of values cannot express.</summary>
    public bool Intermittent => Readings < Total;
}

/// <summary>What one profile's log says about a header name: its values, or that the name never appeared
/// there at all. <c>null</c> is the absence, and it is the point.</summary>
internal readonly record struct ProfileValue(string Profile, string? Value);

/// <summary>One header name across several profiles' logs.</summary>
internal readonly record struct HeaderSpread(string Name, IReadOnlyList<ProfileValue> ByProfile)
{
    /// <summary>At least one profile has never been sent this name while another has.</summary>
    public bool Divergent => ByProfile.Any(p => p.Value is null);
}

/// <summary>
/// The instrument behind T181: a verbatim record of the <c>anthropic-ratelimit-*</c> headers, written to
/// <c>header-probe.jsonl</c> in the profile's own store.
///
/// <para><b>Why a log and not a command.</b> The question is what the overage percentage denominates, and
/// the reading that settles it can only be taken from an account that is actually in overage — a state
/// that arrives unannounced, usually while nobody is running a CLI flag. A one-shot dump answers "what do
/// the headers say right now", which for an account inside its quota is the uninteresting case. So the
/// tray records instead, and the transition is caught whenever it happens.</para>
///
/// <para><b>Why only on a change.</b> At the default cadence a week of polling is thousands of identical
/// lines. A line is written only when the <em>shape</em> moves — a header name appears or disappears, a
/// categorical value changes, or the overage figure crosses zero — which is precisely the set of moments
/// the question is about. The first reading is always kept, as the baseline the rest are changes from.</para>
///
/// <para><b>What one real account sends.</b> The first reading taken (2026-08-03, a subscription account
/// inside its quota) carried fourteen headers, and the set is what settled T181: <c>5h</c>, <c>7d</c> and
/// <c>overage</c> each send the same triple — <c>-utilization</c>, <c>-reset</c>, <c>-status</c> — so the
/// overage figure is a window's utilization like its two neighbours, not a stray number. What separates it
/// is its reset: <c>1788220800</c>, exactly <c>2026-09-01T00:00:00Z</c>, a calendar-month boundary, while
/// the other two roll. Four more headers — <c>unified-status</c>, <c>unified-reset</c>,
/// <c>unified-representative-claim</c> (<c>five_hour</c>) and the <c>unified-fallback</c> pair — are sent
/// and read by nothing here.</para>
///
/// <para><b>What it may hold.</b> Quota metadata: header names and values. No message content, no token —
/// a credential never appears in a response header. This is the same material the tooltip already shows,
/// kept unparsed. Bounded at <see cref="MaxEntries"/> lines, oldest dropped, so it cannot grow without
/// limit on a machine where something flaps.</para>
/// </summary>
internal static class HeaderProbe
{
    /// <summary>Enough to hold every transition of a long overage spell and still be read in one screen.</summary>
    public const int MaxEntries = 500;

    private static string FilePath(string profileKey) =>
        ProfileStore.PathFor(profileKey, "header-probe.jsonl");

    /// <summary>The last shape written, per profile — so the common case (nothing changed) costs no IO
    /// after the first poll of the process.</summary>
    private static readonly Dictionary<string, string> LastShape = new();

    /// <summary>Record this reading if it differs in shape from the previous one. Best-effort: a failure
    /// here must never disturb a poll.</summary>
    /// <returns>true when a line was written.</returns>
    public static bool Record(string profileKey, long nowUnix, IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count == 0) return false;   // a header-less response is an error, not a reading
        try
        {
            string shape = Shape(headers);
            if (!LastShape.TryGetValue(profileKey, out string? known))
            {
                // First poll of this process: the file, not memory, says what was last seen — otherwise
                // every restart writes a duplicate baseline and the log becomes a restart counter.
                known = LastRecordedShape(profileKey);
                if (known != null) LastShape[profileKey] = known;
            }
            if (known == shape) return false;

            string path = FilePath(profileKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, Render(nowUnix, headers) + Environment.NewLine);
            LastShape[profileKey] = shape;
            Trim(path);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Every captured reading, oldest first.</summary>
    public static List<ProbeEntry> Load(string profileKey)
    {
        var list = new List<ProbeEntry>();
        try
        {
            string path = FilePath(profileKey);
            if (!File.Exists(path)) return list;
            foreach (string line in File.ReadLines(path))
                if (line.Length > 0 && TryParse(line, out ProbeEntry e))
                    list.Add(e);
        }
        catch { /* a partial read just yields fewer readings */ }
        list.Sort((a, b) => a.T.CompareTo(b.T));
        return list;
    }

    /// <summary>What makes one reading materially different from the one before it: the header names, the
    /// value of every header that is not a moving number, and whether overage is being consumed at all.
    ///
    /// <para>The split is by suffix rather than by a list of names, because the account that can answer the
    /// remaining half of the question is not this one and its headers are not known in advance. Anything
    /// ending <c>-utilization</c> or <c>-reset</c> is a figure that moves on its own every poll, and keying
    /// on those would make every line a "change" — the log this is designed not to be. Everything else is
    /// categorical: three <c>-status</c> strings, the fallback pair, the representative claim, and whatever
    /// is sent next. A change in any of them is a transition worth a line, and a header nobody here has
    /// seen yet needs no code to be noticed.</para></summary>
    public static string Shape(IReadOnlyDictionary<string, string> headers)
    {
        var names = new List<string>(headers.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);

        bool overage = headers.TryGetValue("anthropic-ratelimit-unified-overage-utilization", out string? o)
                       && double.TryParse(o, System.Globalization.NumberStyles.Float,
                                          System.Globalization.CultureInfo.InvariantCulture, out double v)
                       && v > 0;

        var buf = new System.Text.StringBuilder();
        foreach (string n in names)
        {
            buf.Append(n);
            if (!Moves(n)) buf.Append('=').Append(headers[n]);
            buf.Append(',');
        }
        return buf.Append('|').Append(overage ? '1' : '0').ToString();
    }

    /// <summary>Every value each categorical header has taken across a log — the read-out T210 needed.
    ///
    /// <para><b>Why a summary over a log that is already printed verbatim.</b> The three unsuffixed headers
    /// (<c>unified-status</c>, <c>unified-reset</c>, <c>unified-representative-claim</c>) and the fallback
    /// pair are read by nothing here, and the reason is the same for all of them: their vocabulary is one
    /// value from one account. <c>representative-claim</c> reads <c>five_hour</c>; a mapping written against
    /// that alone has a default arm nobody has seen, which is the guess T181 spent a whole task refusing.
    /// The probe has recorded every one of them since it shipped — but "has a second value ever arrived?"
    /// was a question a person answered by eye, across up to <see cref="MaxEntries"/> readings of fourteen
    /// headers each. This states it: one line per value, and a header that has moved says so.</para>
    ///
    /// <para>Categorical headers only, by the same suffix rule <see cref="Shape"/> keys on — a utilization
    /// or a reset takes a new value every poll, so its "vocabulary" would be the log's length and would
    /// bury the four entries this exists to show. First and last sighting come from the readings
    /// themselves, so an unsorted log yields the same answer as a sorted one.</para></summary>
    public static List<HeaderVocab> Vocabulary(IEnumerable<ProbeEntry> log)
    {
        var seen = new Dictionary<string, Dictionary<string, VocabValue>>(StringComparer.OrdinalIgnoreCase);
        foreach (ProbeEntry e in log)
            foreach (var kv in e.Headers)
            {
                if (Moves(kv.Key)) continue;
                if (!seen.TryGetValue(kv.Key, out var values))
                    seen[kv.Key] = values = new Dictionary<string, VocabValue>(StringComparer.Ordinal);
                values[kv.Value] = values.TryGetValue(kv.Value, out VocabValue v)
                    ? v with { Count = v.Count + 1, First = Math.Min(v.First, e.T), Last = Math.Max(v.Last, e.T) }
                    : new VocabValue(kv.Value, 1, e.T, e.T);
            }

        var result = new List<HeaderVocab>();
        foreach (var kv in seen)
        {
            var values = new List<VocabValue>(kv.Value.Values);
            values.Sort((a, b) => a.First != b.First
                ? a.First.CompareTo(b.First)
                : string.CompareOrdinal(a.Value, b.Value));
            result.Add(new HeaderVocab(kv.Key, values));
        }
        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return result;
    }

    /// <summary>How often each header name was actually sent, over a log's own readings (T211).
    ///
    /// <para>A vocabulary of <em>values</em> cannot express an absence: a name carried by one reading in
    /// seven and a name carried by all seven both read as "one value, seen". That difference is what
    /// settled this — <c>unified-fallback</c> is sent to an account whose overage window exists and not to
    /// one whose organisation has disabled it, while <c>unified-fallback-percentage</c> is sent to both.
    /// So presence is reported over <b>every</b> name, moving figures included: <c>overage-utilization</c>
    /// arriving or going away is the same kind of fact, and the suffix rule that rightly excuses a figure
    /// from having a vocabulary must not excuse it from being counted.</para></summary>
    public static List<HeaderPresence> Presence(IEnumerable<ProbeEntry> log)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        foreach (ProbeEntry e in log)
        {
            total++;
            foreach (string name in e.Headers.Keys)
                counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;
        }

        var result = new List<HeaderPresence>();
        foreach (var kv in counts) result.Add(new HeaderPresence(kv.Key, kv.Value, total));
        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return result;
    }

    /// <summary>Every header name across several profiles' logs, and what each profile has been sent (T211).
    ///
    /// <para>The reading that reframed <c>unified-fallback</c> could not be taken inside one log, because
    /// the two accounts are the two samples: one is offered the header and the other is never sent it at
    /// all. <c>--probe --all</c> printed both logs and left the diff to whoever was reading, which is the
    /// same eyesight T210 removed one level down.</para>
    ///
    /// <para>A profile's value is the joined vocabulary of that name — several values when it has moved,
    /// <c>(figure)</c> for the utilizations and resets, whose value is a number that says nothing when
    /// compared across accounts — and <c>null</c> when the profile has never been sent the name. Null is
    /// the answer this exists to state, so it is a distinct case and not an empty string.</para></summary>
    public static List<HeaderSpread> Spread(IEnumerable<(string Profile, IReadOnlyList<ProbeEntry> Log)> logs)
    {
        var perProfile = new List<(string Profile, Dictionary<string, string> Values)>();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach ((string profile, IReadOnlyList<ProbeEntry> log) in logs)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (HeaderVocab v in Vocabulary(log))
                values[v.Name] = string.Join(" / ", v.Values.Select(x => x.Value));
            // Presence is the wider set: it counts the figures too, and a figure arriving or going away is
            // exactly the kind of divergence this is for.
            foreach (HeaderPresence p in Presence(log))
            {
                names.Add(p.Name);
                if (!values.ContainsKey(p.Name)) values[p.Name] = "(figure)";
            }
            perProfile.Add((profile, values));
        }

        var result = new List<HeaderSpread>();
        foreach (string name in names)
        {
            var by = new List<ProfileValue>();
            foreach ((string profile, Dictionary<string, string> values) in perProfile)
                by.Add(new ProfileValue(profile, values.TryGetValue(name, out string? v) ? v : null));
            result.Add(new HeaderSpread(name, by));
        }
        return result;
    }

    /// <summary>A header whose value is a figure that moves on its own, so only its presence is shape.</summary>
    private static bool Moves(string name) =>
        name.EndsWith("-utilization", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("-reset", StringComparison.OrdinalIgnoreCase);

    private static string? LastRecordedShape(string profileKey)
    {
        List<ProbeEntry> all = Load(profileKey);
        return all.Count == 0 ? null : Shape(all[^1].Headers);
    }

    private static string Render(long nowUnix, IReadOnlyDictionary<string, string> headers)
    {
        var buf = new System.Text.StringBuilder();
        buf.Append(FormattableString.Invariant($"{{\"t\":{nowUnix},\"h\":{{"));
        bool first = true;
        foreach (var kv in headers)
        {
            if (!first) buf.Append(',');
            first = false;
            buf.Append(JsonSerializer.Serialize(kv.Key)).Append(':').Append(JsonSerializer.Serialize(kv.Value));
        }
        return buf.Append("}}").ToString();
    }

    private static bool TryParse(string line, out ProbeEntry e)
    {
        e = default;
        try
        {
            using var doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.Number) return false;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("h", out var h) && h.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty p in h.EnumerateObject())
                    map[p.Name] = p.Value.GetString() ?? "";
            e = new ProbeEntry(t.GetDouble(), map);
            return e.T > 0;
        }
        catch { return false; }
    }

    private static void Trim(string path)
    {
        var lines = new List<string>(File.ReadLines(path));
        if (lines.Count <= MaxEntries) return;
        File.WriteAllLines(path, lines.GetRange(lines.Count - MaxEntries, MaxEntries));
    }
}
