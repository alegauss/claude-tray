namespace ClaudeTray;

/// <summary>
/// What a model charges per million tokens, and the arithmetic that turns a conversation's counted
/// tokens into a <b>list-price equivalent</b> (T346).
///
/// <para><b>What this is not.</b> It is not what anyone pays. A subscription exposes no dollar balance
/// and this app does not know the plan, the seat, the discount or the invoice — §I.7 and T279 both
/// settled that. What it is: the public API rate card applied to tokens already counted, so a $2
/// conversation beside a $40 one is a sentence the token column cannot say. Every string that shows a
/// figure from here says <em>at API list prices</em>, and none of them says <em>cost</em> bare.</para>
///
/// <para><b>The rates carry the date they were read</b> — <see cref="Read"/>. A rate card with no date
/// goes stale silently, which is the one way a number like this lies without anybody editing it. The
/// method note behind the ⓘ prints that date, so a reader can tell a figure from a fossil.</para>
///
/// <para><b>No network (§I.2).</b> The table is compiled in and updated by hand, like every other fact
/// this app states about the outside world.</para>
/// </summary>
internal static class ListPrices
{
    /// <summary>When these rates were read from Anthropic's published pricing. Shown to the reader
    /// rather than kept in a comment: it is the difference between a figure and a fossil.
    /// <para>Read from platform.claude.com/docs/en/about-claude/pricing on 2026-10-27 (T401): every row's
    /// input, output and cache-read rate, and the two cache-write multipliers.</para></summary>
    public static readonly DateOnly Read = new(2026, 10, 27);

    /// <summary>
    /// Base dollars per million tokens, by model id prefix — longest match wins, so a dated id
    /// (<c>claude-opus-4-5-20251101</c>) resolves through its family without an entry per snapshot.
    ///
    /// <para>Ordered longest-first at lookup rather than here, so adding a row never has to be a
    /// decision about where it goes.</para>
    ///
    /// <para><b>The cache-read multiplier is per row (T401).</b> The published card prices a read at 0.1×
    /// input except on the newest models: 0.05× on Opus 5.5 and Sonnet 5.5, 0.025× on Fable 5.1 and Mythos
    /// 5.1. On this machine nearly every turn is Opus 5.5 and cache reads are the largest component, so a
    /// flat 0.1× put most of every figure at twice the card.</para>
    /// </summary>
    private static readonly (string Prefix, double In, double Out, double Read)[] Rates =
    {
        // The 5.1 and 5.5 ids share a prefix with the generation before, so each has a row of its own or it
        // resolves through its predecessor's rate (T399, T401).
        ("claude-fable-5-1",  10.00, 50.00, 0.025),
        ("claude-mythos-5-1", 10.00, 50.00, 0.025),
        ("claude-fable-5",    10.00, 50.00, 0.1),
        ("claude-mythos-5",   10.00, 50.00, 0.1),
        ("claude-opus-5-5",    4.00, 20.00, 0.05),
        ("claude-sonnet-5-5",  2.00, 10.00, 0.05),
        // Up to 100K tokens of prompt, and five times that above it (T402) — the second row is reached only
        // through the key PriceKey gives a call past the line, so the bare id keeps the cheaper one.
        ("claude-haiku-5-5",   0.10,  0.50, 0.1),
        ("claude-haiku-5-5" + LongPrompt, 0.50, 2.50, 0.1),
        ("claude-opus-5",      5.00, 25.00, 0.1),
        ("claude-opus-4-8",    5.00, 25.00, 0.1),
        ("claude-opus-4-7",    5.00, 25.00, 0.1),
        ("claude-opus-4-6",    5.00, 25.00, 0.1),
        ("claude-opus-4-5",    5.00, 25.00, 0.1),
        ("claude-opus-4-1",   15.00, 75.00, 0.1),
        ("claude-opus-4",     15.00, 75.00, 0.1),
        // Introductory at launch, and the standard price since: the move to $3/$15 announced for
        // 2026-09-01 did not happen.
        ("claude-sonnet-5",    2.00, 10.00, 0.1),
        ("claude-sonnet-4-6",  3.00, 15.00, 0.1),
        ("claude-sonnet-4-5",  3.00, 15.00, 0.1),
        ("claude-sonnet-4",    3.00, 15.00, 0.1),
        ("claude-haiku-4-5",   1.00,  5.00, 0.1),
        ("claude-3-5-haiku",   0.80,  4.00, 0.1),
        ("claude-3-haiku",     0.25,  1.25, 0.1),
    };

    /// <summary>
    /// The cache-write multipliers, against a model's own <b>base input</b> rate.
    ///
    /// <para>A five-minute write is 1.25×, a one-hour write 2× — on every model, the 5.5 generation included.
    /// The pair is why T330's TTL split had to land first: a reader that folds both into one rate is out by
    /// 60% on whichever it guessed wrong, and the transcripts state which it was. Reads are per row, in
    /// <see cref="Rates"/>.</para>
    /// </summary>
    private const double CacheWrite5mMultiplier = 1.25;
    private const double CacheWrite1hMultiplier = 2.0;

    /// <summary>What a conversation's tokens come to at list prices, and what the table could not
    /// price.</summary>
    /// <param name="Dollars">The equivalent, in dollars, of every model the table knew.</param>
    /// <param name="PricedTokens">Tokens that went through a rate.</param>
    /// <param name="UnpricedTokens">Tokens belonging to a model this table has no row for — reported
    /// rather than folded in at a guess, so an unknown model makes the figure visibly partial instead
    /// of quietly low. A new model id is exactly the case that would otherwise read as a cheap week.</param>
    internal readonly record struct Equivalent(double Dollars, long PricedTokens, long UnpricedTokens)
    {
        /// <summary>Whether the figure covers everything it was given. False means the reader is being
        /// shown a floor, and the surface has to say so.</summary>
        public bool Complete => UnpricedTokens == 0;
    }

    /// <summary>
    /// Price one conversation's per-model split.
    ///
    /// <para>Cache-write tokens with no TTL stated (<see cref="TokenBits.CacheCreateUnattributed"/>)
    /// are priced at the <b>one-hour</b> rate, which is the dearer of the two. Measured zero on this
    /// machine at T330's first reading, so the branch is about a transcript shape this one has not
    /// produced rather than about the common case — and where it does occur, the expensive guess is the
    /// one that cannot understate.</para>
    /// </summary>
    public static Equivalent Of(IReadOnlyDictionary<string, TokenBits>? perModel)
    {
        if (perModel is null || perModel.Count == 0) return new Equivalent(0, 0, 0);

        double dollars = 0;
        long priced = 0, unpriced = 0;
        foreach (KeyValuePair<string, TokenBits> kv in perModel)
        {
            TokenBits b = kv.Value;
            long all = b.Input + b.Output + b.CacheCreate + b.CacheRead;
            if (RateFor(kv.Key) is not var (inRate, outRate, readMultiplier)) { unpriced += all; continue; }

            dollars += Million(b.Input) * inRate
                     + Million(b.Output) * outRate
                     + Million(b.CacheRead) * inRate * readMultiplier
                     + Million(b.CacheCreate5m) * inRate * CacheWrite5mMultiplier
                     + Million(b.CacheCreate1h + b.CacheCreateUnattributed) * inRate * CacheWrite1hMultiplier;
            priced += all;
        }
        return new Equivalent(dollars, priced, unpriced);
    }

    /// <summary>What a key carries after the model id when its call ran past a long-prompt line (T402).
    /// Not a character a model id contains, so <see cref="ModelOf"/> can take it off again.</summary>
    internal const string LongPrompt = ">100k";

    /// <summary>The prompt size above which Haiku 5.5's dearer row applies: input, cache writes and cache
    /// reads together, which is what the card calls the prompt.</summary>
    internal const long LongPromptTokens = 100_000;

    /// <summary>
    /// The key one call's tokens are summed under (T402): its model id, or the id with
    /// <see cref="LongPrompt"/> when the card prices that model by prompt size and this prompt is past the
    /// line. Decided per call because a session's sums cannot say which of its calls were long.
    /// </summary>
    internal static string PriceKey(string model, TokenBits call) =>
        model.StartsWith("claude-haiku-5-5", StringComparison.OrdinalIgnoreCase)
        && call.Input + call.CacheCreate + call.CacheRead > LongPromptTokens
            ? model + LongPrompt
            : model;

    /// <summary>The model id a price key names — what a list of models shows.</summary>
    internal static string ModelOf(string key) =>
        key.EndsWith(LongPrompt, StringComparison.Ordinal) ? key[..^LongPrompt.Length] : key;

    /// <summary>Whether the table can price this model id at all — the question the Sessions pane asks
    /// before it promises a total.</summary>
    public static bool Knows(string model) => RateFor(model) is not null;

    // Longest prefix wins, so `claude-opus-4-5-20251101` resolves through `claude-opus-4-5` and not
    // through `claude-opus-4`, which is a different generation at three times the rate.
    private static (double In, double Out, double Read)? RateFor(string model)
    {
        (double, double, double)? best = null;
        int bestLen = -1;
        foreach ((string prefix, double i, double o, double r) in Rates)
            if (prefix.Length > bestLen && model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                best = (i, o, r);
                bestLen = prefix.Length;
            }
        return best;
    }

    private static double Million(long tokens) => tokens / 1_000_000.0;
}
