using System;
using System.Collections.Generic;
using System.Globalization;

namespace RetrofitLocalization
{
    /// <summary>Where table files come from. Returns the contents of a file, or null when it does not exist.</summary>
    public interface ITextSource
    {
        string Load(string path);
    }

    /// <summary>File layout and behaviour switches of a <see cref="Localizer"/>.</summary>
    public sealed class LocalizerOptions
    {
        /// <summary>Folder of the string tables: <c>&lt;TableFolder&gt;/&lt;locale&gt;</c>.</summary>
        public string TableFolder = "Localization";

        /// <summary>Phrase tables live next to the string tables: <c>&lt;TableFolder&gt;/&lt;prefix&gt;&lt;locale&gt;</c>.</summary>
        public string PhraseTablePrefix = "phrases_";

        /// <summary>Folder of the pool tables: <c>&lt;PoolFolder&gt;/&lt;table&gt;_&lt;locale&gt;</c>.</summary>
        public string PoolFolder = "Localization/Pools";

        /// <summary>Resolve two-form Korean particles in formatted text while a Korean locale is active.</summary>
        public bool ResolveKoreanParticles = true;

        /// <summary>
        /// Culture used to format arguments. Invariant by default, so a number looks the same on
        /// every device; set a culture here if numbers should follow the active language.
        /// </summary>
        public IFormatProvider FormatProvider = CultureInfo.InvariantCulture;

        public string TablePath(string locale)
        {
            return Combine(TableFolder, locale);
        }

        public string PhraseTablePath(string locale)
        {
            return Combine(TableFolder, PhraseTablePrefix + locale);
        }

        public string PoolTablePath(string table, string locale)
        {
            return Combine(PoolFolder, table + "_" + locale);
        }

        private static string Combine(string folder, string name)
        {
            return string.IsNullOrEmpty(folder) ? name : folder + "/" + name;
        }
    }

    /// <summary>
    /// The lookup engine: tables, fallbacks, plurals, phrases, pools and right-to-left shaping for
    /// one active locale. It has no engine dependency, which is why it can be unit tested with
    /// nothing but in-memory files.
    ///
    /// Two rules shape the API.
    ///
    /// A translated string is only ever something to show. It must never become a save field, a
    /// dictionary key, a product id or an analytics name; those stay in the source language.
    ///
    /// A missing translation never fails and never leaves a label blank. The lookup falls back,
    /// in order, to the source-language text and then to the key itself, and every such fallback
    /// is raised through <see cref="FallbackReported"/> once per locale, so the gap is visible in
    /// a log instead of hiding behind text that happens to be readable.
    ///
    /// Not thread-safe: use from the main thread. The pool registry alone accepts registrations
    /// from other threads.
    /// </summary>
    public sealed class Localizer
    {
        private const string PluralSeparator = ".";

        private readonly LocaleCatalog _catalog;
        private readonly ITextSource _files;
        private readonly LocalizerOptions _options;
        private readonly StringPoolRegistry _pools;

        // Pool tables of the active locale, loaded on first use. A missing file is cached as null so
        // it is looked for once, not once per pool.
        private readonly Dictionary<string, PoolTable> _poolTables =
            new Dictionary<string, PoolTable>(StringComparer.Ordinal);

        private readonly HashSet<FallbackReport> _reported = new HashSet<FallbackReport>();
        private readonly List<FallbackReport> _fallbacks = new List<FallbackReport>();

        private StringTable _source;
        private StringTable _current;
        private PhraseTable _phrases;
        private string _locale;
        private bool _isRtl;
        private bool _isKorean;
        private bool _loaded;
        private IRtlShaper _shaper = BasicArabicShaper.Instance;

        /// <summary>Raised after the active locale changed and every registered pool was rewritten.</summary>
        public event Action LanguageChanged;

        /// <summary>Raised the first time each distinct fallback happens in the active locale.</summary>
        public event Action<FallbackReport> FallbackReported;

        public Localizer(LocaleCatalog catalog, ITextSource files, LocalizerOptions options = null,
            StringPoolRegistry pools = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _options = options ?? new LocalizerOptions();
            _pools = pools ?? new StringPoolRegistry();
            _locale = catalog.SourceLocale;
            _isRtl = catalog.IsRtl(_locale);
            _isKorean = InlineTokens.IsLanguage(_locale, "ko");
        }

        public LocaleCatalog Catalog => _catalog;

        public LocalizerOptions Options => _options;

        public StringPoolRegistry Pools => _pools;

        public string SourceLocale => _catalog.SourceLocale;

        public string CurrentLocale => _locale;

        public bool IsSourceLocaleActive => string.Equals(_locale, _catalog.SourceLocale, StringComparison.Ordinal);

        /// <summary>True when the active locale is written right to left.</summary>
        public bool IsRtl => _isRtl;

        /// <summary>True when the active locale has a non-empty phrase table.</summary>
        public bool HasPhrases
        {
            get
            {
                EnsureLoaded();
                return _phrases != null && _phrases.Count > 0;
            }
        }

        /// <summary>The shaper used for right-to-left locales. Defaults to <see cref="BasicArabicShaper"/>.</summary>
        public IRtlShaper RtlShaper
        {
            get => _shaper;
            set => _shaper = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>The distinct fallbacks reported since the active locale was loaded.</summary>
        public IReadOnlyList<FallbackReport> Fallbacks => _fallbacks;

        // ---- Locale ---------------------------------------------------------------------------

        /// <summary>
        /// Switches the active locale: loads its tables, rewrites every registered pool in place and
        /// then raises <see cref="LanguageChanged"/>. The pools are rewritten before the event so a
        /// subscriber that redraws already reads the new text. Returns false, and changes nothing,
        /// when the locale is not in the catalog.
        /// </summary>
        public bool SetLocale(string locale)
        {
            if (!Load(locale)) return false;
            LanguageChanged?.Invoke();
            return true;
        }

        /// <summary>Same as <see cref="SetLocale"/> without raising <see cref="LanguageChanged"/>: for start-up.</summary>
        public bool Load(string locale)
        {
            if (!_catalog.Contains(locale))
            {
                Report(FallbackKind.UnknownLocale, null, locale ?? string.Empty, "not in the locale catalog");
                return false;
            }

            _reported.Clear();
            _fallbacks.Clear();
            _poolTables.Clear();

            _locale = locale;
            _isRtl = _catalog.IsRtl(locale);
            _isKorean = InlineTokens.IsLanguage(locale, "ko");
            _loaded = true;

            _source = LoadStringTable(SourceLocale);
            _current = IsSourceLocaleActive ? _source : LoadStringTable(locale);
            _phrases = IsSourceLocaleActive ? null : LoadPhraseTable(locale);

            _pools.Refresh(ResolvePool);
            return true;
        }

        /// <summary>Reads the tables of the active locale again, for example after editing a file.</summary>
        public void Reload()
        {
            Load(_locale);
        }

        // ---- Keyed lookup ---------------------------------------------------------------------

        /// <summary>True when the key exists in the active locale or in the source language.</summary>
        public bool Has(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            EnsureLoaded();
            return _current.Contains(key) || _source.Contains(key);
        }

        /// <summary>The text for <paramref name="key"/>, shaped for display in a right-to-left locale.</summary>
        public string Get(string key)
        {
            return ApplyRtl(Resolve(key));
        }

        /// <summary>
        /// The text for <paramref name="key"/> with <c>{0}</c>-style slots filled in. Shaping happens
        /// after formatting so inserted values end up in the right place.
        /// </summary>
        public string Get(string key, params object[] args)
        {
            return ApplyRtl(Format(key, Resolve(key), args));
        }

        /// <summary>
        /// Like <see cref="Get(string)"/> but unshaped. Use it for a value that is about to be passed
        /// as an argument into another lookup: the outer call shapes the whole sentence, and shaping
        /// a fragment twice would scramble it.
        /// </summary>
        public string GetRaw(string key)
        {
            return Resolve(key);
        }

        /// <summary>Like <see cref="Get(string,object[])"/> but unshaped.</summary>
        public string GetRaw(string key, params object[] args)
        {
            return Format(key, Resolve(key), args);
        }

        /// <summary>
        /// The text for <paramref name="key"/>, or <paramref name="defaultText"/> when the key is in
        /// no table. For call sites that already hold authored source-language text and should show
        /// that, not a raw key, while the tables catch up. Still reported.
        /// </summary>
        public string GetOr(string key, string defaultText)
        {
            return ApplyRtl(GetOrRaw(key, defaultText));
        }

        /// <summary>Like <see cref="GetOr"/> but unshaped.</summary>
        public string GetOrRaw(string key, string defaultText)
        {
            if (string.IsNullOrEmpty(key)) return defaultText;
            EnsureLoaded();
            if (_current.TryGet(key, out string value)) return value;
            if (!IsSourceLocaleActive && _source.TryGet(key, out value))
            {
                Report(FallbackKind.MissingKey, null, key);
                return value;
            }

            Report(FallbackKind.UnknownKey, null, key, "the caller's default text is shown");
            return defaultText;
        }

        /// <summary>
        /// The plural form of <paramref name="key"/> for <paramref name="count"/>, formatted with
        /// the count as <c>{0}</c> and <paramref name="moreArgs"/> as <c>{1}</c> onwards.
        ///
        /// Forms are ordinary entries named <c>key.one</c>, <c>key.few</c>, <c>key.many</c>,
        /// <c>key.other</c> and so on, using the categories of <see cref="PluralRules"/>. When the
        /// active locale has no entry at all, the source-language entry is chosen with the source
        /// language's own rule: five items are "many" in Russian but "other" in English.
        /// </summary>
        public string GetPlural(string key, long count, params object[] moreArgs)
        {
            object[] args;
            if (moreArgs == null || moreArgs.Length == 0)
            {
                args = new object[] { count };
            }
            else
            {
                args = new object[moreArgs.Length + 1];
                args[0] = count;
                Array.Copy(moreArgs, 0, args, 1, moreArgs.Length);
            }

            return ApplyRtl(Format(key, ResolvePlural(key, count), args));
        }

        // ---- Lookup by source text ------------------------------------------------------------

        /// <summary>
        /// Translates a piece of source-language text through the phrase table, for call sites that
        /// build a label from pieces. Returns the input when there is no translation, so wrapping an
        /// assignment in this call is always safe. A miss is reported.
        /// </summary>
        public string Translate(string sourceText)
        {
            return ApplyRtl(TranslateRaw(sourceText));
        }

        /// <summary>Like <see cref="Translate"/> but unshaped.</summary>
        public string TranslateRaw(string sourceText)
        {
            if (string.IsNullOrEmpty(sourceText)) return sourceText;
            EnsureLoaded();
            if (IsSourceLocaleActive) return sourceText;
            if (_phrases != null && _phrases.TryTranslate(sourceText, out string translated)) return translated;

            Report(FallbackKind.MissingPhrase, null, sourceText.Trim());
            return sourceText;
        }

        /// <summary>
        /// The quiet variant for sweeps, which look up every string on screen and expect most of
        /// them to miss. Returns false, and reports nothing, when there is no translation.
        /// </summary>
        public bool TryTranslate(string sourceText, out string translated)
        {
            translated = null;
            if (string.IsNullOrEmpty(sourceText)) return false;
            EnsureLoaded();
            if (_phrases == null || !_phrases.TryTranslate(sourceText, out string raw)) return false;
            translated = ApplyRtl(raw);
            return true;
        }

        // ---- Pools ----------------------------------------------------------------------------

        /// <summary>
        /// Localizes a string pool in place and returns the same array.
        ///
        /// Meant to be called from a static field initializer, so it runs once when the owning type
        /// is first used:
        /// <code>
        ///   static readonly string[] Tips = Loc.LocalizePool("Tips", "loading", new[] { "...", "..." });
        /// </code>
        /// The array is registered, and every later language change overwrites its contents.
        ///
        /// Precondition: pass an inline literal, or an explicit copy. The contents of the array you
        /// pass are replaced by translations, so an array that is also used for logic (compared
        /// against, used as a lookup key) would silently stop matching after a language change.
        ///
        /// The pool holds text in typing order. For a right-to-left locale, call
        /// <see cref="ApplyRtl"/> where an entry is finally assigned to a label, after any formatting.
        /// </summary>
        public string[] LocalizePool(string table, string key, string[] source)
        {
            if (source == null) return null;
            EnsureLoaded();
            string[] snapshot = _pools.Track(table, key, source);
            if (!IsSourceLocaleActive) StringPoolRegistry.Write(source, ResolvePool(table, key, snapshot));
            return source;
        }

        /// <summary>List-shaped twin of <see cref="LocalizePool(string,string,string[])"/>.</summary>
        public List<string> LocalizePool(string table, string key, List<string> source)
        {
            if (source == null) return null;
            EnsureLoaded();
            string[] snapshot = _pools.Track(table, key, source);
            if (!IsSourceLocaleActive) StringPoolRegistry.Write(source, ResolvePool(table, key, snapshot));
            return source;
        }

        /// <summary>
        /// Rewrites every registered pool for the active locale again. A language change does this by
        /// itself; call it after pools were registered without being localized (see
        /// <see cref="StringPoolRegistry.Track(string,string,string[])"/>).
        /// </summary>
        public void RefreshPools()
        {
            EnsureLoaded();
            _pools.Refresh(ResolvePool);
        }

        /// <summary>
        /// Reads one pool table of one locale straight from the files, uncached and untouched, for
        /// audits. Returns null when the file does not exist. Throws <see cref="TableFormatException"/>.
        /// </summary>
        public PoolTable LoadPoolTable(string table, string locale)
        {
            string json = _files.Load(_options.PoolTablePath(table, locale));
            return json == null ? null : PoolTable.Parse(table, locale, json);
        }

        // ---- Right-to-left --------------------------------------------------------------------

        /// <summary>
        /// Shapes <paramref name="text"/> for display when the active locale is right-to-left;
        /// otherwise returns it unchanged. Rich-text tags are preserved.
        /// </summary>
        public string ApplyRtl(string text)
        {
            return _isRtl ? RtlText.Apply(text, _shaper) : text;
        }

        // ---- Reporting ------------------------------------------------------------------------

        /// <summary>Reports a fallback detected outside this class, such as a missing asset variant.</summary>
        public void ReportFallback(FallbackKind kind, string table, string key, string detail = null)
        {
            Report(kind, table, key, detail);
        }

        private void Report(FallbackKind kind, string table, string key, string detail = null)
        {
            var report = new FallbackReport(kind, _locale, table, key, detail);
            if (!_reported.Add(report)) return;
            _fallbacks.Add(report);
            FallbackReported?.Invoke(report);
        }

        // ---- Internals ------------------------------------------------------------------------

        private void EnsureLoaded()
        {
            if (!_loaded) Load(_locale);
        }

        // Active locale, then the source language, then the key itself: readable text in every case.
        private string Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            EnsureLoaded();
            if (_current.TryGet(key, out string value)) return value;
            if (!IsSourceLocaleActive && _source.TryGet(key, out value))
            {
                Report(FallbackKind.MissingKey, null, key);
                return value;
            }

            Report(FallbackKind.UnknownKey, null, key);
            return key;
        }

        private string ResolvePlural(string key, long count)
        {
            if (string.IsNullOrEmpty(key)) return key;
            EnsureLoaded();
            if (TryGetPluralForm(_current, key, count, out string value)) return value;
            if (!IsSourceLocaleActive && TryGetPluralForm(_source, key, count, out value))
            {
                Report(FallbackKind.MissingKey, null, key);
                return value;
            }

            Report(FallbackKind.UnknownKey, null, key);
            return key;
        }

        private bool TryGetPluralForm(StringTable table, string key, long count, out string value)
        {
            PluralCategory category = PluralRules.GetCategory(table.Locale, count);
            string form = key + PluralSeparator + PluralRules.Suffix(category);
            if (table.TryGet(form, out value)) return true;

            if (category != PluralCategory.Other
                && table.TryGet(key + PluralSeparator + PluralRules.Suffix(PluralCategory.Other), out value))
            {
                Report(FallbackKind.MissingPluralForm, null, form);
                return true;
            }

            // A key without plural forms still works as a plain format string.
            return table.TryGet(key, out value);
        }

        private string Format(string key, string template, object[] args)
        {
            if (template == null || args == null || args.Length == 0) return template;

            if (!Placeholders.TryFormat(_options.FormatProvider, template, args, out string text))
            {
                Report(FallbackKind.FormatError, null, key,
                    "the text cannot be formatted with " + args.Length.ToString(CultureInfo.InvariantCulture)
                    + " argument(s)");
                return template;
            }

            return _isKorean && _options.ResolveKoreanParticles ? KoreanParticles.Resolve(text) : text;
        }

        // The text a pool should hold in the active locale. Returns the source array itself whenever
        // the pool stays in the source language.
        private string[] ResolvePool(string table, string key, string[] source)
        {
            if (IsSourceLocaleActive) return source;

            PoolTable poolTable = GetPoolTable(table);
            if (poolTable == null) return source;

            if (!poolTable.TryGet(key, out string[] localized) || localized == null)
            {
                Report(FallbackKind.MissingPool, table, key);
                return source;
            }

            if (localized.Length != source.Length)
            {
                // Pools are often indexed in parallel with another pool, so a translation of a
                // different length cannot be used even partially.
                Report(FallbackKind.PoolLengthMismatch, table, key,
                    localized.Length.ToString(CultureInfo.InvariantCulture) + " entries, the source has "
                    + source.Length.ToString(CultureInfo.InvariantCulture));
                return source;
            }

            // Blank entries fall back one by one. The cached table is copied first, never edited, so
            // an audit reading the same table still sees the blanks.
            string[] merged = null;
            for (int i = 0; i < localized.Length; i++)
            {
                if (!string.IsNullOrEmpty(localized[i])) continue;
                if (merged == null) merged = (string[])localized.Clone();
                merged[i] = source[i];
                Report(FallbackKind.EmptyPoolElement, table,
                    key + "[" + i.ToString(CultureInfo.InvariantCulture) + "]");
            }

            return merged ?? localized;
        }

        private PoolTable GetPoolTable(string table)
        {
            if (_poolTables.TryGetValue(table, out PoolTable cached)) return cached;

            PoolTable loaded = null;
            string path = _options.PoolTablePath(table, _locale);
            string json = _files.Load(path);
            if (json == null)
            {
                Report(FallbackKind.MissingPoolTable, table, path);
            }
            else
            {
                try
                {
                    loaded = PoolTable.Parse(table, _locale, json);
                }
                catch (TableFormatException e)
                {
                    Report(FallbackKind.MalformedTable, table, path, e.Message);
                }
            }

            _poolTables[table] = loaded;
            return loaded;
        }

        private StringTable LoadStringTable(string locale)
        {
            string path = _options.TablePath(locale);
            string json = _files.Load(path);
            if (json == null)
            {
                Report(FallbackKind.MissingTable, null, path);
                return StringTable.Empty(locale);
            }

            try
            {
                return StringTable.Parse(locale, json);
            }
            catch (TableFormatException e)
            {
                Report(FallbackKind.MalformedTable, null, path, e.Message);
                return StringTable.Empty(locale);
            }
        }

        // A project that does not use phrase tables has none, so a missing file is not a fallback.
        private PhraseTable LoadPhraseTable(string locale)
        {
            string path = _options.PhraseTablePath(locale);
            string json = _files.Load(path);
            if (json == null) return null;

            try
            {
                return PhraseTable.Parse(locale, json);
            }
            catch (TableFormatException e)
            {
                Report(FallbackKind.MalformedTable, null, path, e.Message);
                return null;
            }
        }
    }
}
