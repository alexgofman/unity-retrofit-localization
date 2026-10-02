using UnityEditor;
using UnityEngine;

namespace RetrofitLocalization.Editor
{
    /// <summary>
    /// Switches the language from the Editor without going through an in-game picker.
    ///
    /// In Play mode a button switches the running game, which is the quickest way to watch a live
    /// language change. In Edit mode it writes the same PlayerPrefs key the game writes, so the
    /// next Play session starts in that language. Clearing the saved choice gives the state of a
    /// player who has never picked a language.
    ///
    /// The buttons come from the locale catalog in the settings; there is no second list here.
    /// </summary>
    public sealed class LocaleSwitcherWindow : EditorWindow
    {
        private LocaleCatalog _catalog;
        private RetrofitLocalizationSettings _catalogOwner;
        private Vector2 _scroll;

        [MenuItem(RetrofitLocalizationMenu.Root + "Locale Switcher", priority = 0)]
        private static void Open()
        {
            GetWindow<LocaleSwitcherWindow>(false, "Locale Switcher", true);
        }

        private void OnFocus()
        {
            _catalog = null; // the settings may have been edited while the window was in the background
        }

        private void OnGUI()
        {
            RetrofitLocalizationSettings settings = RetrofitLocalizationMenu.CurrentSettings;
            if (_catalog == null || _catalogOwner != settings)
            {
                _catalog = settings.BuildCatalog();
                _catalogOwner = settings;
            }

            string prefsKey = settings.PlayerPrefsKey;
            string saved = PlayerPrefs.GetString(prefsKey, string.Empty);
            bool playing = Application.isPlaying;

            EditorGUILayout.HelpBox(playing
                    ? "Play mode: a button switches the running game and saves the choice."
                    : "Edit mode: a button saves the choice; the next Play session starts in that language.",
                MessageType.Info);

            EditorGUILayout.LabelField("Saved choice", string.IsNullOrEmpty(saved) ? "(none)" : saved);
            if (playing) EditorGUILayout.LabelField("Active locale", LocalizationService.CurrentLocale);
            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            string current = playing ? LocalizationService.CurrentLocale : saved;
            foreach (LocaleInfo locale in _catalog.All)
            {
                string caption = locale.NativeName + "  (" + locale.Code + ")";
                if (locale.Code == _catalog.SourceLocale) caption += "  - source";

                using (new EditorGUI.DisabledScope(locale.Code == current))
                {
                    if (GUILayout.Button(caption)) Select(locale.Code, prefsKey);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(saved)))
            {
                if (GUILayout.Button("Clear saved choice (first-run state)")) ClearSaved(prefsKey);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate Tables")) TableValidator.Run();
                if (GUILayout.Button("Audit String Pools")) PoolAuditor.Run(true);
            }
        }

        private void Select(string code, string prefsKey)
        {
            if (Application.isPlaying)
            {
                LocalizationService.SetLocale(code);
            }
            else
            {
                PlayerPrefs.SetString(prefsKey, code);
                PlayerPrefs.Save();
                // Drop anything an Edit-mode tool loaded for the previous choice.
                LocalizationService.Reset();
            }

            Repaint();
        }

        private void ClearSaved(string prefsKey)
        {
            PlayerPrefs.DeleteKey(prefsKey);
            PlayerPrefs.Save();
            if (!Application.isPlaying) LocalizationService.Reset();
            Repaint();
        }
    }
}
