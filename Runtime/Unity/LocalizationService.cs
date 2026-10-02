using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace RetrofitLocalization
{
    /// <summary>
    /// The static entry point. A code base that was never written for localization has no place to
    /// inject a service into its thousands of call sites, so the facade is static on purpose: a
    /// call site changes from <c>label.text = "Play"</c> to
    /// <c>label.text = LocalizationService.Get("menu.play")</c> and nothing else.
    ///
    /// All logic lives in <see cref="Localizer"/>, which has no engine dependency. This class adds
    /// what only the engine can provide: reading tables from Resources, remembering the player's
    /// choice in PlayerPrefs and detecting the device language.
    ///
    /// The choice of language is a display preference. It is stored in PlayerPrefs, never in a
    /// save file, and a translated string must never be used as a save field, a lookup key, a
    /// product id or an analytics name.
    ///
    /// Use from the main thread. <see cref="LocalizePool(string,string,string[])"/> is the one
    /// exception: it may be reached from a static initializer that runs anywhere.
    /// </summary>
    public static class LocalizationService
    {
        // Deliberately not cleared by ResetStatics. With domain reload disabled, static readonly
        // pool fields keep the arrays they were given in an earlier play session and their
        // initializers do not run again, so the registry is the only thing that still knows those
        // arrays. The next start refreshes them for whatever locale the new session begins in.
        private static readonly StringPoolRegistry s_pools = new StringPoolRegistry();

        private static Localizer s_engine;
        private static RetrofitLocalizationSettings s_settings;
        private static ITextSource s_textSource;
        private static IRtlShaper s_shaper;
        private static bool s_logFallbacks;
        private static int s_mainThreadId;

        // A pool was registered at a moment when the tables could not be read; see LocalizePool.
        private static bool s_poolsPending;

        /// <summary>
        /// Raised after the language changed and every registered pool was rewritten. Subscribe from
        /// <c>OnEnable</c> and unsubscribe in <c>OnDisable</c>; a static subscriber should subscribe
        /// in a <c>RuntimeInitializeOnLoadMethod</c> that runs after subsystem registration, because
        /// the event is cleared there when a play session starts. A subscriber that throws is logged
        /// and does not keep the others from running.
        /// </summary>
        public static event Action OnLanguageChanged;

        /// <summary>Raised the first time each distinct fallback happens in the active locale.</summary>
        public static event Action<FallbackReport> OnFallback;

        // Statics outlive a play session when domain reload is disabled in the Enter Play Mode
        // options. Everything a session must not inherit from the previous one is dropped here.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Reset();
            s_poolsPending = false;
            s_shaper = null;
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            OnLanguageChanged = null;
            OnFallback = null;
        }

        // Pools that survived from an earlier session (see s_pools) still hold that session's
        // language. Bring them in line before the first Awake can read one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RefreshSurvivingPools()
        {
            if (s_pools.Count > 0) Init();
        }

        // ---- Setup ----------------------------------------------------------------------------

        /// <summary>The settings in use: the ones given to <see cref="Configure"/>, else the asset, else defaults.</summary>
        public static RetrofitLocalizationSettings Settings
        {
            get
            {
                if (s_settings == null) s_settings = RetrofitLocalizationSettings.LoadOrDefault();
                return s_settings;
            }
        }

        /// <summary>The engine behind the facade, for anything the static methods do not expose.</summary>
        public static Localizer Engine
        {
            get
            {
                Init();
                return s_engine;
            }
        }

        public static bool IsInitialized => s_engine != null;

        public static string CurrentLocale => Engine.CurrentLocale;

        public static string SourceLocale => Engine.SourceLocale;

        public static LocaleCatalog Catalog => Engine.Catalog;

        /// <summary>
        /// Supplies settings built in code, and optionally a different place to read tables from.
        /// Meant for start-up, before the first lookup. Calling it later starts over with the new
        /// settings without telling anybody; follow it with <see cref="Reload"/> if text is already
        /// on screen.
        /// </summary>
        public static void Configure(RetrofitLocalizationSettings settings, ITextSource textSource = null)
        {
            Reset();
            s_settings = settings;
            s_textSource = textSource;
        }

        /// <summary>
        /// Loads the tables for the saved, detected or source locale. Optional: every lookup calls it
        /// on first use, so no call site can run ahead of initialisation.
        /// </summary>
        public static void Init()
        {
            if (s_engine == null) CreateEngine();
            if (s_poolsPending) RefreshPendingPools();
        }

        /// <summary>
        /// Reads the tables of the active locale again and raises <see cref="OnLanguageChanged"/>,
        /// so everything on screen is redrawn as after a language change. For table files edited
        /// while the game runs, and for a <see cref="Configure"/> call made in the middle of a session.
        /// </summary>
        public static void Reload()
        {
            Engine.Reload();
            RaiseLanguageChanged();
        }

        /// <summary>
        /// Forgets the loaded tables and any configuration, so the next lookup initialises again.
        /// Registered pools stay registered and are brought in line by that next initialisation.
        /// No event is raised: this is for start-up, tear-down and tests.
        /// </summary>
        public static void Reset()
        {
            if (s_engine != null)
            {
                s_engine.FallbackReported -= HandleFallback;
                s_engine.LanguageChanged -= RaiseLanguageChanged;
            }

            s_engine = null;
            s_settings = null;
            s_textSource = null;
        }

        /// <summary>
        /// Replaces the built-in Arabic shaper, for example with an adapter around a full shaping library.
        /// </summary>
        public static void SetRtlShaper(IRtlShaper shaper)
        {
            s_shaper = shaper;
            if (s_engine != null) s_engine.RtlShaper = shaper ?? BasicArabicShaper.Instance;
        }

        // ---- Language -------------------------------------------------------------------------

        /// <summary>
        /// Switches the language now and remembers the choice. Order matters and is fixed: the
        /// choice is saved, the tables are loaded, every registered pool is rewritten in place, and
        /// only then is <see cref="OnLanguageChanged"/> raised, so a subscriber that redraws already
        /// reads the new text. Returns false, and changes nothing, for a code that is not in the catalog.
        /// </summary>
        public static bool SetLocale(string code)
        {
            Localizer engine = Engine;
            if (engine.Catalog.Contains(code))
            {
                PlayerPrefs.SetString(Settings.PlayerPrefsKey, code);
                PlayerPrefs.Save();
            }

            return engine.SetLocale(code);
        }

        /// <summary>
        /// Removes the saved choice without changing the active language. The next start then
        /// behaves like a first run.
        /// </summary>
        public static void ClearSavedLocale()
        {
            PlayerPrefs.DeleteKey(Settings.PlayerPrefsKey);
            PlayerPrefs.Save();
        }

        // ---- Lookups (see Localizer for the details of each) ----------------------------------

        public static string Get(string key)
        {
            return Engine.Get(key);
        }

        public static string Get(string key, params object[] args)
        {
            return Engine.Get(key, args);
        }

        public static string GetRaw(string key)
        {
            return Engine.GetRaw(key);
        }

        public static string GetRaw(string key, params object[] args)
        {
            return Engine.GetRaw(key, args);
        }

        public static string GetOr(string key, string defaultText)
        {
            return Engine.GetOr(key, defaultText);
        }

        public static string GetOrRaw(string key, string defaultText)
        {
            return Engine.GetOrRaw(key, defaultText);
        }

        public static string GetPlural(string key, long count, params object[] moreArgs)
        {
            return Engine.GetPlural(key, count, moreArgs);
        }

        public static bool Has(string key)
        {
            return Engine.Has(key);
        }

        public static string Translate(string sourceText)
        {
            return Engine.Translate(sourceText);
        }

        public static string TranslateRaw(string sourceText)
        {
            return Engine.TranslateRaw(sourceText);
        }

        public static string ApplyRtl(string text)
        {
            return Engine.ApplyRtl(text);
        }

        /// <summary>
        /// Localizes a string pool in place and returns the same array. Typical use is a one-line
        /// shim next to the pools of a class:
        /// <code>
        ///   static string[] L(string key, string[] source) =&gt; LocalizationService.LocalizePool("Tips", key, source);
        ///   static readonly string[] Loading = L("loading", new[] { "First tip", "Second tip" });
        /// </code>
        /// Pass an inline literal: the contents of the array are overwritten on every language
        /// change. See <see cref="Localizer.LocalizePool(string,string,string[])"/>.
        ///
        /// A static initializer runs wherever its type is first touched. For a pool owned by a
        /// MonoBehaviour or ScriptableObject that can be while the engine is constructing the object,
        /// possibly on a loading thread, where Resources and PlayerPrefs are off limits. In that case
        /// the pool is only registered, keeps its source text for the moment, and is localized by the
        /// next lookup made from ordinary code. Pools owned by plain classes are localized at once.
        /// </summary>
        public static string[] LocalizePool(string table, string key, string[] source)
        {
            if (source == null) return null;

            if (IsMainThread())
            {
                try
                {
                    return Engine.LocalizePool(table, key, source);
                }
                catch (UnityException)
                {
                    // Thrown by the engine API when it is called from a constructor or field initializer.
                }
            }

            s_pools.Track(table, key, source);
            s_poolsPending = true;
            return source;
        }

        /// <summary>List-shaped twin of <see cref="LocalizePool(string,string,string[])"/>.</summary>
        public static List<string> LocalizePool(string table, string key, List<string> source)
        {
            if (source == null) return null;

            if (IsMainThread())
            {
                try
                {
                    return Engine.LocalizePool(table, key, source);
                }
                catch (UnityException)
                {
                    // See the array overload.
                }
            }

            s_pools.Track(table, key, source);
            s_poolsPending = true;
            return source;
        }

        /// <summary>
        /// Loads the variant of a Resources asset for the active locale, "<paramref name="basePath"/>_de",
        /// and falls back to the asset at <paramref name="basePath"/>. For content that is translated
        /// as whole files (a JSON document, an image with text in it) rather than string by string.
        /// A missing variant is reported like any other fallback.
        /// </summary>
        public static T LoadLocalizedAsset<T>(string basePath) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(basePath)) return null;

            Localizer engine = Engine;
            if (!engine.IsSourceLocaleActive)
            {
                T variant = Resources.Load<T>(basePath + "_" + engine.CurrentLocale);
                if (variant != null) return variant;
                engine.ReportFallback(FallbackKind.MissingAssetVariant, null, basePath);
            }

            return Resources.Load<T>(basePath);
        }

        // ---- Internals ------------------------------------------------------------------------

        // Builds and loads the engine. s_engine is only kept if every engine call on the way
        // succeeds, so an attempt made where the engine API is off limits (see LocalizePool) leaves
        // nothing half-initialised behind.
        private static void CreateEngine()
        {
            RetrofitLocalizationSettings settings = Settings;
            var engine = new Localizer(settings.BuildCatalog(), s_textSource ?? new ResourcesTextSource(),
                settings.BuildOptions(), s_pools);
            if (s_shaper != null) engine.RtlShaper = s_shaper;

            string saved = PlayerPrefs.GetString(settings.PlayerPrefsKey, string.Empty);
            bool savedIsUsable = !string.IsNullOrEmpty(saved) && engine.Catalog.Contains(saved);
            string start = savedIsUsable ? saved : DetectOrSource(settings, engine.Catalog);

            s_logFallbacks = settings.LogFallbacks && (Application.isEditor || Debug.isDebugBuild);
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId; // engine calls only succeed there
            engine.FallbackReported += HandleFallback;
            engine.LanguageChanged += RaiseLanguageChanged;

            // Assigned before the tables are read, so a fallback handler that looks something up
            // while they load finds this engine instead of starting another one.
            s_engine = engine;
            bool loaded = false;
            try
            {
                engine.Load(start); // reads the tables and rewrites every registered pool
                loaded = true;
            }
            finally
            {
                if (!loaded) s_engine = null;
            }

            s_poolsPending = false;

            if (!string.IsNullOrEmpty(saved) && !savedIsUsable)
            {
                // A locale that was removed from the catalog since the player chose it.
                engine.ReportFallback(FallbackKind.UnknownLocale, null, saved, "saved choice is no longer in the catalog");
            }
        }

        private static void RefreshPendingPools()
        {
            s_engine.RefreshPools(); // if this throws, the flag stays set and the next lookup tries again
            s_poolsPending = false;
        }

        private static string DetectOrSource(RetrofitLocalizationSettings settings, LocaleCatalog catalog)
        {
            if (settings.AutoDetectSystemLocale)
            {
                string detected = catalog.FindBySystemLanguage(Application.systemLanguage.ToString());
                if (detected != null) return detected;
            }

            return catalog.SourceLocale;
        }

        // Zero means "not known yet": the first successful start records the thread it ran on.
        private static bool IsMainThread()
        {
            return s_mainThreadId == 0 || Thread.CurrentThread.ManagedThreadId == s_mainThreadId;
        }

        private static void RaiseLanguageChanged()
        {
            Action subscribers = OnLanguageChanged;
            if (subscribers == null) return;

            // One by one: the language has already changed, so a subscriber that fails must not
            // leave the ones after it showing the old language.
            foreach (Delegate subscriber in subscribers.GetInvocationList())
            {
                try
                {
                    ((Action)subscriber)();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private static void HandleFallback(FallbackReport report)
        {
            OnFallback?.Invoke(report);
            if (s_logFallbacks) Debug.LogWarning("[RetrofitLocalization] " + report);
        }
    }
}
