using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows.Markup;

namespace ClaudeTray;

/// <summary>
/// Lightweight, dependency-free localization for the whole app. The display language is picked once
/// at startup: a saved preference in Settings (<see cref="Apply"/>) wins, otherwise it follows the
/// operating system's UI language (<see cref="CultureInfo.CurrentUICulture"/>). English is the default
/// and the fallback for any key a translation is missing.
///
/// Each language is a plain JSON file under <c>lang\</c> (e.g. <c>lang\fr.json</c>), embedded in the
/// assembly as a resource — so translations travel inside the single self-contained .exe with zero
/// extra publish steps, yet each language is one file a translator can edit without touching C#. The
/// files may carry <c>//</c> comments (skipped when parsed). Code reads a string with
/// <see cref="T(string)"/> / <see cref="T(string, object[])"/>; XAML reads one with <c>{local:Loc Key}</c>.
///
/// The language is fixed for the process lifetime once applied (changing the Settings preference
/// prompts a restart), which is why the XAML extension can resolve at parse time. <b>To add a language:
/// drop a <c>lang\&lt;code&gt;.json</c> file (copy <c>en.json</c> and translate) and add one row to
/// <see cref="Languages"/> — detection, the Settings picker, validation and date formatting all follow.</b>
/// </summary>
internal static class L
{
    internal enum Lang { En, PtBr, PtPt, Fr, Es }

    /// <summary>One shipped language. <c>Code</c> is the value stored in Settings; <c>Culture</c> formats
    /// dates (empty = invariant, i.e. English); <c>Endonym</c> names it in the picker (in its own
    /// language); <c>Matches</c> auto-detects it from the OS UI culture (matchers are mutually exclusive,
    /// so their order is irrelevant). Strings live in the embedded <c>lang\{Code}.json</c>.</summary>
    private sealed record LangInfo(Lang Lang, string Code, string Culture, string Endonym, Func<CultureInfo, bool> Matches);

    // The shipped languages, in Settings-picker order. English is the base/fallback and is never
    // auto-matched (it's the default when nothing else matches). pt-PT is matched exactly so a generic
    // pt-* OS still falls to pt-BR.
    private static readonly LangInfo[] Languages =
    {
        new(Lang.En,   "en",    "",      "English",              _ => false),
        new(Lang.PtBr, "pt-BR", "pt-BR", "Português (Brasil)",   c => Two(c) == "pt" && !Named(c, "pt-PT")),
        new(Lang.PtPt, "pt-PT", "pt-PT", "Português (Portugal)", c => Named(c, "pt-PT")),
        new(Lang.Fr,   "fr",    "fr-FR", "Français",             c => Two(c) == "fr"),
        new(Lang.Es,   "es",    "es-ES", "Español",              c => Two(c) == "es"),
    };

    private static Lang? _current;

    /// <summary>The active language. Defaults to the OS UI language on first use; a saved preference
    /// overrides it via <see cref="Apply"/>, called once at startup before any window is built.</summary>
    public static Lang Current => _current ??= Detect();

    /// <summary>Map a saved preference (a language code, or <c>"auto"</c>/null/unknown) to a language
    /// without changing the active one: a known code wins, anything else follows the OS.</summary>
    public static Lang Resolve(string? pref)
    {
        foreach (LangInfo li in Languages)
            if (li.Code == pref) return li.Lang;
        return Detect();
    }

    /// <summary>Apply a saved preference as the active language. Must run before any XAML with
    /// <c>{local:Loc}</c> is parsed (the extension resolves at parse time), i.e. at the top of startup.</summary>
    public static void Apply(string? pref) => _current = Resolve(pref);

    /// <summary>Whether <paramref name="pref"/> is a value the language setting accepts — a known
    /// language code or <c>"auto"</c>. Used by <see cref="Settings"/> to validate a loaded/edited value.</summary>
    public static bool IsValidPreference(string? pref) =>
        pref == "auto" || Languages.Any(li => li.Code == pref);

    /// <summary>The languages offered in the Settings picker, each as (code, own-language name).</summary>
    public static IReadOnlyList<(string Code, string Name)> PickerLanguages =>
        Languages.Select(li => (li.Code, li.Endonym)).ToArray();

    /// <summary>Every shipped language's code, the base (<c>en</c>) first. This is the set
    /// <c>--selftest</c> compares (T185), so a language added to <see cref="Languages"/> is checked
    /// without a second list being edited.</summary>
    public static IReadOnlyList<string> Codes => Languages.Select(li => li.Code).ToArray();

    /// <summary>The parsed table behind one shipped language code, for the key and placeholder comparison
    /// in <c>--selftest</c> (T185). Unlike <see cref="T(string)"/> this does <b>not</b> fall back to
    /// English — the gaps between the files are the thing being measured. An unknown code yields an
    /// empty table.</summary>
    public static IReadOnlyDictionary<string, string> Strings(string code)
    {
        foreach (LangInfo li in Languages)
            if (li.Code == code) return Table(li.Lang);
        return new Dictionary<string, string>();
    }

    /// <summary>The culture to format <b>dates and clock times</b> in for the active language (invariant
    /// for English). Named for what it is allowed to reach: numbers are invariant everywhere and go
    /// through <see cref="Nums"/>, so a call site naming this one for a figure is visibly wrong (T216).</summary>
    public static CultureInfo DateCulture
    {
        get
        {
            foreach (LangInfo li in Languages)
                if (li.Lang == Current && li.Culture.Length > 0)
                    return CultureInfo.GetCultureInfo(li.Culture);
            return CultureInfo.InvariantCulture;
        }
    }

    private static Lang Detect()
    {
        try
        {
            // CurrentUICulture follows the Windows display language (GetUserDefaultUILanguage), which is
            // what "the OS default language" means to a user. First matching language wins; English is
            // the fallback when nothing matches.
            CultureInfo c = CultureInfo.CurrentUICulture;
            foreach (LangInfo li in Languages)
                if (li.Lang != Lang.En && li.Matches(c)) return li.Lang;
        }
        catch { /* fall through to English */ }
        return Lang.En;
    }

    /// <summary>The localized string for <paramref name="key"/>, falling back to English then the key.</summary>
    public static string T(string key)
    {
        if (Current != Lang.En && Table(Current).TryGetValue(key, out string? s))
            return s;
        return Table(Lang.En).TryGetValue(key, out string? en) ? en : key;
    }

    /// <summary>A localized format string filled with <paramref name="args"/> (see <see cref="T(string)"/>).</summary>
    public static string T(string key, params object[] args) => string.Format(T(key), args);

    // Parsed string tables, loaded from the embedded JSON on first use and cached for the process.
    private static readonly Dictionary<Lang, Dictionary<string, string>> _tables = new();

    // Lenient parse: allow // comments (the files are organized with section comments) and a trailing
    // comma, so hand-editing a translation can't trip on either.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static Dictionary<string, string> Table(Lang lang)
    {
        if (_tables.TryGetValue(lang, out Dictionary<string, string>? t)) return t;
        Dictionary<string, string> loaded = Load(CodeOf(lang));
        _tables[lang] = loaded;
        return loaded;
    }

    private static string CodeOf(Lang lang)
    {
        foreach (LangInfo li in Languages)
            if (li.Lang == lang) return li.Code;
        return "en";
    }

    // Load and parse the embedded lang\{code}.json (resource "ClaudeTray.lang.{code}.json"). Matched by
    // suffix so a RootNamespace change can't silently break it. Any failure yields an empty table — the
    // language simply falls back to English key by key, which never crashes a lookup.
    private static Dictionary<string, string> Load(string code)
    {
        try
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string? name = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith($".{code}.json", StringComparison.OrdinalIgnoreCase));
            if (name is null) return new();

            using Stream? stream = asm.GetManifestResourceStream(name);
            if (stream is null) return new();

            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    private static string Two(CultureInfo c) => c.TwoLetterISOLanguageName.ToLowerInvariant();
    private static bool Named(CultureInfo c, string name) => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// XAML markup extension that resolves a localized string at parse time: <c>{local:Loc Key}</c>.
/// The language is fixed for the process lifetime, so resolving once at load is correct — there is
/// no runtime language switch to react to.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
internal sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    /// <summary>The lookup key (e.g. <c>settings.title</c>).</summary>
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
