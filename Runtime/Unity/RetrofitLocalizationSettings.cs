using System;
using System.Collections.Generic;
using UnityEngine;

namespace RetrofitLocalization
{
    /// <summary>Which labels <see cref="UITextLocalizer"/> looks at on its own.</summary>
    public enum UiSweepMode
    {
        /// <summary>No automatic sweep. Labels are only translated through keys or explicit calls.</summary>
        Disabled,

        /// <summary>Only hierarchies passed to <see cref="UITextLocalizer.RegisterRoot"/>.</summary>
        RegisteredRoots,

        /// <summary>
        /// Every active root canvas, plus registered roots. Needs no call-site changes at all, which
        /// is the point of a retrofit.
        /// </summary>
        AllRootCanvases
    }

    /// <summary>
    /// Project settings: the locale catalog and where the tables live. One asset is read by the
    /// runtime and by every Editor tool, so there is a single list of languages to keep up to date.
    ///
    /// Create it with Tools > Retrofit Localization > Create Settings Asset; it has to be in a
    /// Resources folder under the name <see cref="ResourceName"/>. Without an asset the defaults
    /// below apply, which describe a project that only has its source language.
    /// Settings can also be built in code and passed to <see cref="LocalizationService.Configure"/>.
    /// </summary>
    [CreateAssetMenu(fileName = ResourceName, menuName = "Retrofit Localization/Settings", order = 1000)]
    public sealed class RetrofitLocalizationSettings : ScriptableObject
    {
        public const string ResourceName = "RetrofitLocalizationSettings";

        /// <summary>One language of the catalog, as edited in the inspector.</summary>
        [Serializable]
        public sealed class Locale
        {
            [Tooltip("Locale code. Also the file name of its string table.")]
            public string code;

            [Tooltip("Name shown in a language picker, written in the language itself.")]
            public string nativeName;

            [Tooltip("Name for logs and analytics.")]
            public string englishName;

            [Tooltip("Right-to-left script. Text is shaped before it is displayed.")]
            public bool rightToLeft;

            [Tooltip("Device languages that select this locale on first run.")]
            public SystemLanguage[] systemLanguages;

            public Locale()
            {
            }

            public Locale(string code, string nativeName, string englishName, bool rightToLeft = false,
                params SystemLanguage[] systemLanguages)
            {
                this.code = code;
                this.nativeName = nativeName;
                this.englishName = englishName;
                this.rightToLeft = rightToLeft;
                this.systemLanguages = systemLanguages;
            }
        }

        [Header("Locales")]
        [Tooltip("The language the project was written in. It is the fallback for every other locale.")]
        [SerializeField] private string _sourceLocale = "en";

        [SerializeField] private List<Locale> _locales = new List<Locale>
        {
            new Locale("en", "English", "English", false, SystemLanguage.English)
        };

        [Tooltip("Off: a player who never picked a language stays on the source language, exactly as before "
                 + "localization was added. On: first run follows the device language.")]
        [SerializeField] private bool _autoDetectSystemLocale;

        [Tooltip("PlayerPrefs key that stores the player's choice.")]
        [SerializeField] private string _playerPrefsKey = "retrofit_localization.locale";

        [Header("Table locations (below any Resources folder)")]
        [Tooltip("String tables: <folder>/<locale>.json")]
        [SerializeField] private string _tableFolder = "Localization";

        [Tooltip("Phrase tables sit next to the string tables: <folder>/<prefix><locale>.json")]
        [SerializeField] private string _phraseTablePrefix = "phrases_";

        [Tooltip("Pool tables: <folder>/<table>_<locale>.json")]
        [SerializeField] private string _poolFolder = "Localization/Pools";

        [Header("UI sweep")]
        [SerializeField] private UiSweepMode _uiSweepMode = UiSweepMode.AllRootCanvases;

        [Tooltip("Seconds between two sweeps while a translated locale is active.")]
        [Min(0.1f)]
        [SerializeField] private float _uiSweepInterval = 0.6f;

        [Header("Diagnostics (Editor and development builds only)")]
        [Tooltip("Log every fallback to the console once per locale.")]
        [SerializeField] private bool _logFallbacks = true;

        [Tooltip("Write displayed text that looks like a label but has no phrase-table entry to a text file in the "
                 + "persistent data path. The filter is a heuristic: expect some text that needs no translation.")]
        [SerializeField] private bool _collectUntranslatedPhrases;

        [Header("Pool audit (Editor)")]
        [Tooltip("Namespaces whose types own string pools. The audit runs their static constructors so every pool "
                 + "registers. Leave empty to audit only the pools that are already registered.")]
        [SerializeField] private string[] _poolNamespaces = new string[0];

        [Tooltip("Where the audit writes its full report, relative to the project folder.")]
        [SerializeField] private string _auditReportPath = "Logs/RetrofitLocalizationPoolAudit.txt";

        [Tooltip("Optional list of reviewed exceptions, relative to the project folder. One id per line: "
                 + "Table|Key or Table|Key[index].")]
        [SerializeField] private string _auditIgnoreFile = string.Empty;

        public string SourceLocale
        {
            get => _sourceLocale;
            set => _sourceLocale = value;
        }

        /// <summary>The catalog as edited. Use <see cref="BuildCatalog"/> for the validated form.</summary>
        public List<Locale> Locales => _locales;

        public bool AutoDetectSystemLocale
        {
            get => _autoDetectSystemLocale;
            set => _autoDetectSystemLocale = value;
        }

        public string PlayerPrefsKey
        {
            get => _playerPrefsKey;
            set => _playerPrefsKey = value;
        }

        public string TableFolder
        {
            get => _tableFolder;
            set => _tableFolder = value;
        }

        public string PhraseTablePrefix
        {
            get => _phraseTablePrefix;
            set => _phraseTablePrefix = value;
        }

        public string PoolFolder
        {
            get => _poolFolder;
            set => _poolFolder = value;
        }

        public UiSweepMode UiSweepMode
        {
            get => _uiSweepMode;
            set => _uiSweepMode = value;
        }

        public float UiSweepInterval
        {
            get => _uiSweepInterval;
            set => _uiSweepInterval = value;
        }

        public bool LogFallbacks
        {
            get => _logFallbacks;
            set => _logFallbacks = value;
        }

        public bool CollectUntranslatedPhrases
        {
            get => _collectUntranslatedPhrases;
            set => _collectUntranslatedPhrases = value;
        }

        public string[] PoolNamespaces
        {
            get => _poolNamespaces;
            set => _poolNamespaces = value;
        }

        public string AuditReportPath
        {
            get => _auditReportPath;
            set => _auditReportPath = value;
        }

        public string AuditIgnoreFile
        {
            get => _auditIgnoreFile;
            set => _auditIgnoreFile = value;
        }

        private static RetrofitLocalizationSettings s_defaults;

        /// <summary>The settings asset from Resources, or an in-memory instance with the defaults.</summary>
        public static RetrofitLocalizationSettings LoadOrDefault()
        {
            RetrofitLocalizationSettings settings = Resources.Load<RetrofitLocalizationSettings>(ResourceName);
            if (settings != null) return settings;

            // One shared instance, so asking repeatedly (an Editor window does) creates nothing new.
            if (s_defaults == null) s_defaults = CreateInstance<RetrofitLocalizationSettings>();
            return s_defaults;
        }

        /// <summary>
        /// Builds the catalog the runtime uses. Entries that cannot be used (no code, a code listed
        /// twice) are skipped with a warning rather than failing at start-up, and the source locale
        /// is added if the list forgot it.
        /// </summary>
        public LocaleCatalog BuildCatalog()
        {
            string source = string.IsNullOrEmpty(_sourceLocale) ? "en" : _sourceLocale;
            var locales = new List<LocaleInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (_locales != null)
            {
                foreach (Locale locale in _locales)
                {
                    if (locale == null || string.IsNullOrEmpty(locale.code))
                    {
                        Debug.LogWarning("[RetrofitLocalization] A locale without a code was skipped.", this);
                        continue;
                    }

                    if (!seen.Add(locale.code))
                    {
                        Debug.LogWarning("[RetrofitLocalization] Locale '" + locale.code + "' is listed twice; "
                                         + "the second entry was skipped.", this);
                        continue;
                    }

                    locales.Add(new LocaleInfo(locale.code, locale.nativeName, locale.englishName, locale.rightToLeft,
                        SystemLanguageNames(locale.systemLanguages)));
                }
            }

            if (!seen.Contains(source))
            {
                Debug.LogWarning("[RetrofitLocalization] The source locale '" + source + "' is not in the locale "
                                 + "list; it was added.", this);
                locales.Insert(0, new LocaleInfo(source, source, source));
            }

            return new LocaleCatalog(source, locales);
        }

        /// <summary>The file layout part of the settings, in the form the engine takes.</summary>
        public LocalizerOptions BuildOptions()
        {
            return new LocalizerOptions
            {
                TableFolder = TrimSlashes(_tableFolder),
                PhraseTablePrefix = _phraseTablePrefix ?? string.Empty,
                PoolFolder = TrimSlashes(_poolFolder)
            };
        }

        private static List<string> SystemLanguageNames(SystemLanguage[] languages)
        {
            var names = new List<string>();
            if (languages == null) return names;
            foreach (SystemLanguage language in languages) names.Add(language.ToString());
            return names;
        }

        private static string TrimSlashes(string folder)
        {
            return string.IsNullOrEmpty(folder) ? string.Empty : folder.Trim('/', '\\');
        }
    }
}
