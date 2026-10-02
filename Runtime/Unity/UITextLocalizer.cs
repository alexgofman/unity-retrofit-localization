using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RetrofitLocalization
{
    /// <summary>
    /// Translates the labels of an existing UI at the point where they are displayed, for all the
    /// text that was never given a key: captions baked into prefabs and scenes, and strings that
    /// code assigns as literals.
    ///
    /// Whatever source-language string a label currently shows is looked up in the phrase table of
    /// the active locale and swapped for its translation; a string that is not in the table is left
    /// exactly as it is. Nothing but <c>Text.text</c> and <c>TMP_Text.text</c> is ever written.
    ///
    /// How it stays safe in a live project:
    /// - While the source language is active, or the active locale has no phrase table, there is no
    ///   runner object, no coroutine and no sweep. The original build behaves as it always did.
    /// - Text a player typed is never touched: the text component of an input field is skipped.
    /// - A TextMeshPro label that is filled through <c>SetText(format, ...)</c> or a char array is
    ///   skipped, because its <c>text</c> property does not report what the label shows.
    /// - A label whose text is read back by code as data can be excluded with <see cref="Exempt"/>.
    ///
    /// Language changes are followed live. Every label that was handled keeps its original text
    /// (see <see cref="LabelTracker{TLabel}"/>), so switching from one translation to another, or
    /// back to the source language, rewrites the labels that are on screen without a restart.
    /// </summary>
    [AddComponentMenu("")] // created by code, not meant to be added by hand
    [DisallowMultipleComponent]
    public sealed class UITextLocalizer : MonoBehaviour
    {
        private const string RunnerName = "RetrofitLocalization.UITextLocalizer";
        private const string UntranslatedFileName = "retrofit-localization-untranslated.txt";
        private const int MinEntriesBeforePruning = 64;
        private const float MinSweepInterval = 0.1f;

        // A view often fills its lists a moment after it appears, so a newly registered root is
        // swept again after these delays (seconds, unscaled).
        private static readonly float[] FollowUpDelays = { 0.2f, 0.5f, 1f };

        private static readonly LabelTracker<Component> s_labels = new LabelTracker<Component>();
        private static readonly HashSet<Component> s_exempt = new HashSet<Component>();
        private static readonly HashSet<GameObject> s_roots = new HashSet<GameObject>();
        private static readonly UntranslatedPhraseLog s_untranslated = new UntranslatedPhraseLog();

        // Reused buffers, so that sweeping a hierarchy does not allocate a list each time.
        private static readonly List<Text> s_texts = new List<Text>();
        private static readonly List<TMP_Text> s_tmpTexts = new List<TMP_Text>();
        private static readonly List<Component> s_labelBuffer = new List<Component>();
        private static readonly List<GameObject> s_rootBuffer = new List<GameObject>();

        private static readonly Func<string, string> s_translate = TranslateOrNull;
        private static readonly Func<string, string> s_noTranslation = _ => null;
        private static readonly Predicate<Component> s_isDestroyedLabel = label => label == null;
        private static readonly Predicate<GameObject> s_isDestroyedRoot = root => root == null;

        private static UITextLocalizer s_runner;
        private static bool s_enabled;
        private static bool s_collectUntranslated;
        private static int s_pruneRootsAt = MinEntriesBeforePruning;
        private static int s_pruneExemptAt = MinEntriesBeforePruning;

        // The engine and locale the tracked labels were last brought in line with. Every sweep
        // compares them with the service, so the labels follow the language however it changed,
        // whether an event announced it or not.
        private static Localizer s_syncedEngine;
        private static string s_syncedLocale;

        // ---- Lifetime -------------------------------------------------------------------------

        // See LocalizationService.ResetStatics: needed when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Shutdown();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            LocalizationService.Init();
            if (LocalizationService.Settings.UiSweepMode != UiSweepMode.Disabled) Enable();
        }

        /// <summary>True while sweeping is switched on and the active locale has a phrase table.</summary>
        public static bool IsActive => s_enabled && LocalizationService.Engine.HasPhrases;

        /// <summary>Number of labels whose original text is being remembered.</summary>
        public static int TrackedLabelCount => s_labels.Count;

        /// <summary>
        /// Switches sweeping on. Called automatically at start-up unless the settings set the sweep
        /// mode to <see cref="UiSweepMode.Disabled"/>; call it yourself in that case if you want to
        /// sweep registered roots only. From here on a language change is followed by the labels on
        /// screen.
        /// </summary>
        public static void Enable()
        {
            // Unsubscribe first so that calling this twice never subscribes twice.
            LocalizationService.OnLanguageChanged -= HandleLanguageChanged;
            LocalizationService.OnLanguageChanged += HandleLanguageChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;

            s_enabled = true;
            SweepAll();
        }

        /// <summary>
        /// Switches sweeping off and puts every handled label back to its original text. Registered
        /// roots and exemptions are forgotten.
        /// </summary>
        public static void Disable()
        {
            RestoreAll();
            Shutdown();
        }

        // Drops everything without touching any label. Runs when a play session starts and when it
        // ends, so nothing of one session stays subscribed or remembered in the next one, or in
        // Edit mode afterwards.
        private static void Shutdown()
        {
            LocalizationService.OnLanguageChanged -= HandleLanguageChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Application.quitting -= Shutdown;

            s_enabled = false;
            if (s_runner != null && Application.isPlaying) Destroy(s_runner.gameObject);
            s_runner = null;
            s_syncedEngine = null;
            s_syncedLocale = null;

            s_labels.Clear();
            s_exempt.Clear();
            s_roots.Clear();
            s_untranslated.Clear();
            s_pruneRootsAt = MinEntriesBeforePruning;
            s_pruneExemptAt = MinEntriesBeforePruning;
        }

        // ---- Public API -----------------------------------------------------------------------

        /// <summary>
        /// Tells the localizer about a hierarchy that just appeared: a view, a popup, a list cell.
        /// It is swept at once and again shortly after, and stays known so a later language change
        /// reaches it. In <see cref="UiSweepMode.AllRootCanvases"/> mode this only speeds things up;
        /// in <see cref="UiSweepMode.RegisteredRoots"/> mode it is how labels are found at all.
        /// Destroyed roots are forgotten automatically.
        /// </summary>
        public static void RegisterRoot(GameObject root)
        {
            if (root == null) return;
            s_roots.Add(root);

            // While nothing is being translated no sweep runs, so roots that were destroyed in the
            // meantime are dropped here, each time the set has doubled.
            if (s_roots.Count >= s_pruneRootsAt)
            {
                s_roots.RemoveWhere(s_isDestroyedRoot);
                s_pruneRootsAt = Math.Max(MinEntriesBeforePruning, s_roots.Count * 2);
            }

            if (!Synchronise()) return;

            SweepTree(root);
            if (s_runner != null) s_runner.StartCoroutine(s_runner.FollowUp(root));
        }

        public static void UnregisterRoot(GameObject root)
        {
            if (!ReferenceEquals(root, null)) s_roots.Remove(root);
        }

        /// <summary>
        /// Excludes a label for good. Use it for a label whose displayed text is read back by code,
        /// for example a value shown on a button and parsed again when a form is saved: swapping
        /// that text would corrupt the value.
        /// </summary>
        public static void Exempt(Component label)
        {
            if (label == null) return;

            if (s_labels.TryGetSource(label, out string source))
            {
                // Put the original text back, but only if the label still shows what the sweep
                // wrote. If code has assigned something since, that newer text is the one to keep.
                if (TryGetText(label, out string current) && s_labels.IsUpToDate(label, current))
                {
                    SetText(label, source);
                }

                s_labels.Forget(label);
            }

            AddExempt(label);
        }

        /// <summary>Translates the labels under <paramref name="root"/> now. Does nothing while inactive.</summary>
        public static void Sweep(GameObject root)
        {
            if (root == null || !Synchronise()) return;
            SweepTree(root);
        }

        /// <summary>
        /// Sweeps everything the configured <see cref="UiSweepMode"/> covers. The runner calls this
        /// on a timer; call it yourself after building UI if you do not want to wait for the timer.
        /// </summary>
        public static void SweepAll()
        {
            if (!Synchronise()) return;

            s_labels.Prune(s_isDestroyedLabel);
            s_exempt.RemoveWhere(s_isDestroyedLabel);
            s_roots.RemoveWhere(s_isDestroyedRoot);

            // Only in Play mode: in Edit mode this would write translations into the open scene.
            if (Application.isPlaying && LocalizationService.Settings.UiSweepMode == UiSweepMode.AllRootCanvases)
            {
                // Catches UI however it was created, including UI that existed before this class
                // woke up. Cheap when repeated, because an unchanged label is skipped.
                Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < canvases.Length; i++)
                {
                    if (canvases[i].isRootCanvas) SweepTree(canvases[i].gameObject);
                }
            }

            s_rootBuffer.Clear();
            s_rootBuffer.AddRange(s_roots);
            for (int i = 0; i < s_rootBuffer.Count; i++) SweepTree(s_rootBuffer[i]);
            s_rootBuffer.Clear();
        }

        /// <summary>
        /// Labels seen on screen that look like interface text but have no phrase-table entry.
        /// Collected only when the settings ask for it, in the Editor and in development builds.
        /// </summary>
        public static List<string> GetUntranslatedPhrases()
        {
            return s_untranslated.ToSortedList();
        }

        /// <summary>Where the collected phrases are written while the application runs.</summary>
        public static string UntranslatedPhrasesPath => Path.Combine(Application.persistentDataPath, UntranslatedFileName);

        // ---- Keeping in line with the service -------------------------------------------------

        private static void HandleLanguageChanged()
        {
            // An explicit announcement also covers reloaded tables, where engine and locale are
            // the same as before, so the comparison in Synchronise must not skip the work.
            s_syncedEngine = null;
            SweepAll();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SweepAll();
        }

        // Brings the tracked labels and the runner in line with the service and returns whether
        // there is anything to sweep. Called at the start of every sweep, which makes the sweep
        // self-correcting: it does not depend on having been told about a change.
        private static bool Synchronise()
        {
            if (!s_enabled) return false;

            Localizer engine = LocalizationService.Engine;
            if (!ReferenceEquals(engine, s_syncedEngine)
                || !string.Equals(engine.CurrentLocale, s_syncedLocale, StringComparison.Ordinal))
            {
                s_syncedEngine = engine;
                s_syncedLocale = engine.CurrentLocale;

                RetrofitLocalizationSettings settings = LocalizationService.Settings;
                s_collectUntranslated = settings.CollectUntranslatedPhrases && (Application.isEditor || Debug.isDebugBuild);

                // Every label handled so far is translated again from its remembered original
                // text. Where the new locale has no translation, and always for the source
                // language, that puts the original text back.
                RetranslateTracked(s_translate);

                // Without a phrase table nothing is translated any more, so nothing has to be remembered.
                if (!engine.HasPhrases) s_labels.Clear();
            }

            bool hasPhrases = engine.HasPhrases;
            UpdateRunner(hasPhrases);
            return hasPhrases;
        }

        private static void RetranslateTracked(Func<string, string> translate)
        {
            s_labels.CopyLabelsTo(s_labelBuffer);
            for (int i = 0; i < s_labelBuffer.Count; i++)
            {
                Component label = s_labelBuffer[i];
                if (label == null || !TryGetText(label, out string current))
                {
                    s_labels.Forget(label);
                    continue;
                }

                string text = s_labels.Retranslate(label, current, translate);
                if (text != null) SetText(label, text);
            }

            s_labelBuffer.Clear();
        }

        // Puts back the original text of every label that still shows what the sweep wrote.
        private static void RestoreAll()
        {
            RetranslateTracked(s_noTranslation);
            s_labels.Clear();
        }

        // ---- One hierarchy, one label ---------------------------------------------------------

        private static void SweepTree(GameObject root)
        {
            if (root == null) return;

            root.GetComponentsInChildren(true, s_texts);
            for (int i = 0; i < s_texts.Count; i++) Process(s_texts[i], s_texts[i].text);
            s_texts.Clear();

            root.GetComponentsInChildren(true, s_tmpTexts);
            for (int i = 0; i < s_tmpTexts.Count; i++)
            {
                TMP_Text label = s_tmpTexts[i];
                if (s_exempt.Contains(label)) continue;

                if (TryReadText(label, out string current))
                {
                    Process(label, current);
                }
                else
                {
                    s_labels.Forget(label);
                    AddExempt(label);
                }
            }

            s_tmpTexts.Clear();
        }

        private static void Process(Component label, string current)
        {
            if (string.IsNullOrEmpty(current)) return;
            if (s_labels.IsUpToDate(label, current)) return; // the common case: nothing changed
            if (s_exempt.Contains(label)) return;

            // The text is new or has changed, so look again at what kind of label this is. A label
            // can become an input field's text, or be given a key, after it was first seen.
            if (IsTypedText(label) || IsKeyDriven(label))
            {
                s_labels.Forget(label);
                AddExempt(label);
                return;
            }

            string text = s_labels.Sweep(label, current, s_translate);
            if (text != null) SetText(label, text);
        }

        // The component an input field renders the typed value into. Its placeholder is ordinary
        // interface text and is translated like any other label.
        private static bool IsTypedText(Component label)
        {
            InputField legacyField = label.GetComponentInParent<InputField>(true);
            if (legacyField != null && legacyField.textComponent == label) return true;

            TMP_InputField tmpField = label.GetComponentInParent<TMP_InputField>(true);
            return tmpField != null && tmpField.textComponent == label;
        }

        // A label that takes its text from a key already follows the language on its own.
        private static bool IsKeyDriven(Component label)
        {
            return label.GetComponent<LocalizedText>() != null;
        }

        // Reads what a TextMeshPro label shows, or returns false when that cannot be known.
        //
        // TextMeshPro keeps text that was set through SetText(format, ...), a StringBuilder or a
        // char array in a buffer of its own. In a player build the text property returns that
        // buffer once and after that the string that was last assigned, which is no longer what
        // the label shows. Two reads that differ are the sign of such a label. Swapping its stale
        // string for a translation would overwrite the value on screen, so it is left alone.
        private static bool TryReadText(TMP_Text label, out string text)
        {
            text = label.text;
            string again = label.text;
            return ReferenceEquals(text, again) || string.Equals(text, again, StringComparison.Ordinal);
        }

        private static bool TryGetText(Component label, out string text)
        {
            if (label is Text legacy)
            {
                text = legacy.text;
                return true;
            }

            return TryReadText((TMP_Text)label, out text);
        }

        private static void SetText(Component label, string text)
        {
            if (label is Text legacy) legacy.text = text;
            else ((TMP_Text)label).text = text;
        }

        private static void AddExempt(Component label)
        {
            s_exempt.Add(label);
            if (s_exempt.Count < s_pruneExemptAt) return;

            s_exempt.RemoveWhere(s_isDestroyedLabel);
            s_pruneExemptAt = Math.Max(MinEntriesBeforePruning, s_exempt.Count * 2);
        }

        private static string TranslateOrNull(string source)
        {
            Localizer engine = LocalizationService.Engine;
            if (engine.TryTranslate(source, out string translated)) return translated;

            // Only a locale that has a phrase table can be missing a phrase.
            if (s_collectUntranslated && engine.HasPhrases) s_untranslated.Record(source);
            return null;
        }

        // ---- Runner ---------------------------------------------------------------------------

        // The runner is only a host for the timer. It exists exactly while there is something to
        // sweep, which keeps the source-language build free of it. It lives in the
        // DontDestroyOnLoad scene and goes away with the play session.
        private static void UpdateRunner(bool hasPhrases)
        {
            bool wanted = hasPhrases && Application.isPlaying;
            if (wanted && s_runner == null)
            {
                var host = new GameObject(RunnerName);
                DontDestroyOnLoad(host);
                s_runner = host.AddComponent<UITextLocalizer>();
            }
            else if (!wanted && s_runner != null)
            {
                if (Application.isPlaying) Destroy(s_runner.gameObject);
                s_runner = null;
            }
        }

        private void OnEnable()
        {
            StartCoroutine(SweepLoop());
        }

        private IEnumerator SweepLoop()
        {
            yield return null; // let the scene finish building its UI first

            float interval = 0f;
            WaitForSecondsRealtime wait = null;
            while (true)
            {
                try
                {
                    SweepAll();
                    WriteUntranslatedPhrases();
                }
                catch (Exception e)
                {
                    // A sweep that fails once must not end sweeping for the rest of the session.
                    Debug.LogException(e);
                }

                // Unscaled time: a paused game (time scale 0) still shows menus that need translating.
                float wanted = Mathf.Max(MinSweepInterval, LocalizationService.Settings.UiSweepInterval);
                if (wait == null || !Mathf.Approximately(wanted, interval))
                {
                    interval = wanted;
                    wait = new WaitForSecondsRealtime(interval);
                }

                yield return wait;
            }
        }

        private IEnumerator FollowUp(GameObject root)
        {
            yield return null;
            Sweep(root);
            for (int i = 0; i < FollowUpDelays.Length; i++)
            {
                yield return new WaitForSecondsRealtime(FollowUpDelays[i]);
                Sweep(root);
            }
        }

        private static void WriteUntranslatedPhrases()
        {
            if (!s_collectUntranslated || !s_untranslated.IsDirty) return;

            try
            {
                File.WriteAllLines(UntranslatedPhrasesPath, s_untranslated.ToSortedList());
                s_untranslated.MarkSaved();
            }
            catch (IOException e)
            {
                s_collectUntranslated = false; // do not retry every sweep
                Debug.LogWarning("[RetrofitLocalization] Could not write " + UntranslatedPhrasesPath + ": " + e.Message);
            }
            catch (UnauthorizedAccessException e)
            {
                s_collectUntranslated = false;
                Debug.LogWarning("[RetrofitLocalization] Could not write " + UntranslatedPhrasesPath + ": " + e.Message);
            }
        }
    }
}
