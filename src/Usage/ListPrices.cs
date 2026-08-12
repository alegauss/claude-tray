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
    /// rather than kept in a comment: it is the difference between a figure and a fossil.</summary>
    public static readonly DateOnly Read = new(2026, 6, 24);

    /// <summary>
    /// Base dollars per million tokens, by model id prefix — longest match wins, so a dated id
    /// (<c>claude-opus-4-5-20251101</c>) resolves through its family without an entry per snapshot.
    ///
    /// <para>Ordered longest-first at lookup rather than here, so adding a row never has to be a
    /// decision about where it goes.</para>
    ///
    /// <para><b>One rate is deliberately the standard one.</b> Claude Sonnet 5 carries an introductory
    /// $2/$10 through 2026-08-31; this table holds its standard $3/$15, so a Sonnet 5 conversation
    /// inside that window reads high rather than reading low. An equivalent that errs upward is a
    /// figure a reader can discount; one that errs downward is a figure that flatters.</para>
    /// </summary>
    private static readonly (string Prefix, double In, double Out)[] Rates =
    {
        ("claude-fable-5",   10.00, 50.00),
        ("claude-mythos-5",  10.00, 50.00),
        ("claude-opus-5",     5.00, 25.00),
        ("claude-opus-4-8",   5.00, 25.00),
        ("claude-opus-4-7",   5.00, 25.00),
        ("claude-opus-4-6",   5.00, 25.00),
        ("claude-opus-4-5",   5.00, 25.00),
        ("claude-opus-4-1",  15.00, 75.00),
        ("claude-opus-4",    15.00, 75.00),
        ("claude-sonnet-5",   3.00, 15.00),
        ("claude-sonnet-4-6", 3.00, 15.00),
        ("claude-sonnet-4-5", 3.00, 15.00),
        ("claude-sonnet-4",   3.00, 15.00),
        ("claude-haiku-4-5",  1.00,  5.00),
        ("claude-3-5-haiku",  0.80,  4.00),
        ("claude-3-haiku",    0.25,  1.25),
    };

    /// <summary>
    /// The cache multipliers, against a model's own <b>base input</b> rate — the reason this is per
    /// model rather than a flat table of four numbers.
    ///
    /// <para>A read is 0.1×, a five-minute write 1.25×, a one-hour write 2×. The pair of write rates is
    /// why T330's TTL split had to land first: a reader that folds both into one rate is out by 60% on
    /// whichever it guessed wrong, and the transcripts state which it was.</para>
    /// </summary>
    private const double CacheReadMultiplier = 0.1;
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
            if (RateFor(kv.Key) is not var (inRate, outRate)) { unpriced += all; continue; }

            dollars += Million(b.Input) * inRate
                     + Million(b.Output) * outRate
                     + Million(b.CacheRead) * inRate * CacheReadMultiplier
                     + Million(b.CacheCreate5m) * inRate * CacheWrite5mMultiplier
                     + Million(b.CacheCreate1h + b.CacheCreateUnattributed) * inRate * CacheWrite1hMultiplier;
            priced += all;
        }
        return new Equivalent(dollars, priced, unpriced);
    }

    /// <summary>Whether the table can price this model id at all — the question the Sessions pane asks
    /// before it promises a total.</summary>
    public static bool Knows(string model) => RateFor(model) is not null;

    // Longest prefix wins, so `claude-opus-4-5-20251101` resolves through `claude-opus-4-5` and not
    // through `claude-opus-4`, which is a different generation at three times the rate.
    private static (double In, double Out)? RateFor(string model)
    {
        (double, double)? best = null;
        int bestLen = -1;
        foreach ((string prefix, double i, double o) in Rates)
            if (prefix.Length > bestLen && model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                best = (i, o);
                bestLen = prefix.Length;
            }
        return best;
    }

    private static double Million(long tokens) => tokens / 1_000_000.0;
}
