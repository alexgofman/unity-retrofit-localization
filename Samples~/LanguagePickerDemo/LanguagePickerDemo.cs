using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RetrofitLocalization.Samples
{
    /// <summary>
    /// A self-contained demo. Add this component to an empty GameObject in an empty scene and press
    /// Play; it builds its own canvas. The buttons along the top switch the language while
    /// everything stays on screen:
    ///
    /// - labels bound to a key through <see cref="LocalizedText"/>,
    /// - labels set from code with <see cref="LocalizationService.Get(string,object[])"/>, including
    ///   plural forms, Korean particles and a rich-text tag inside Arabic text,
    /// - labels that hold plain English literals and are translated by the label sweep,
    /// - a line from a <c>static readonly</c> string pool that is rewritten in place.
    ///
    /// The labels are legacy <see cref="Text"/> components with the built-in font. That font falls
    /// back to operating-system fonts for glyphs it lacks, so the five scripts should show up
    /// without any font setup. A TextMeshPro label would need a font asset that contains the glyphs
    /// of each language, including the Arabic presentation forms (U+FE70 to U+FEFC).
    ///
    /// The demo hands its own settings to the service and resets the service when it is destroyed,
    /// so try it in a scratch scene, not in a scene of a project that already has localization set
    /// up. Its translations are illustrative and have not been reviewed by native speakers.
    /// </summary>
    public sealed class LanguagePickerDemo : MonoBehaviour
    {
        private const string TableFolder = "RetrofitLocalizationDemo/Tables";
        private const string PoolFolder = "RetrofitLocalizationDemo/Pools";
        private const string UserName = "Tester";
        private const float LineHeight = 34f;
        private const float BarHeight = 104f;

        // Counts that land in different plural categories across the five languages.
        private static readonly int[] Counts = { 0, 1, 2, 3, 5, 11, 21, 102 };

        private readonly List<Text> _labels = new List<Text>();
        private readonly Dictionary<string, Button> _localeButtons = new Dictionary<string, Button>();
        private RetrofitLocalizationSettings _settings;
        private Font _font;
        private RectTransform _bar;
        private RectTransform _content;
        private float _nextY;
        private int _countIndex = 1;
        private int _tipIndex;

        private Text _countCaption;
        private Text _fallbacks;
        private Text _greeting;
        private Text _page;
        private Text _opened;
        private Text _filed;
        private Text _price;
        private Text _files;
        private Text _tasks;
        private Text _options;
        private Text _status;
        private Text _dialog;
        private Text _unknown;
        private Text _tip;
        private Text _poolGreeting;

        // Awake runs before the package looks for its settings, so this is the place to supply them.
        private void Awake()
        {
            _settings = ScriptableObject.CreateInstance<RetrofitLocalizationSettings>();
            _settings.SourceLocale = "en";
            _settings.TableFolder = TableFolder;
            _settings.PoolFolder = PoolFolder;
            _settings.PlayerPrefsKey = "retrofit_localization.demo.locale";
            // Only the demo's own canvas is swept, whatever else the scene contains.
            _settings.UiSweepMode = UiSweepMode.RegisteredRoots;

            // Native names are written as escapes so this file reads the same in every editor:
            // Deutsch, Русский, 한국어, العربية.
            _settings.Locales.Clear();
            _settings.Locales.Add(new RetrofitLocalizationSettings.Locale("en", "English", "English", false, SystemLanguage.English));
            _settings.Locales.Add(new RetrofitLocalizationSettings.Locale("de", "Deutsch", "German", false, SystemLanguage.German));
            _settings.Locales.Add(new RetrofitLocalizationSettings.Locale("ru", "\u0420\u0443\u0441\u0441\u043A\u0438\u0439", "Russian", false, SystemLanguage.Russian));
            _settings.Locales.Add(new RetrofitLocalizationSettings.Locale("ko", "\uD55C\uAD6D\uC5B4", "Korean", false, SystemLanguage.Korean));
            _settings.Locales.Add(new RetrofitLocalizationSettings.Locale("ar", "\u0627\u0644\u0639\u0631\u0628\u064A\u0629", "Arabic", true, SystemLanguage.Arabic));

            LocalizationService.Configure(_settings);
        }

        private void OnEnable()
        {
            LocalizationService.OnLanguageChanged += Refresh;
        }

        private void OnDisable()
        {
            LocalizationService.OnLanguageChanged -= Refresh;
        }

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildCanvas();
            BuildPicker();

            // 1. Bound to a key. The component follows the language by itself.
            AddKeyed("demo.title", 26, FontStyle.Bold);
            AddKeyed("demo.subtitle", 16, FontStyle.Italic);

            // 2. Set from code. Refresh() runs again whenever the language changes.
            AddKeyed("section.keyed", 18, FontStyle.Bold);
            _greeting = AddLabel(string.Empty);
            _page = AddLabel(string.Empty);
            _opened = AddLabel(string.Empty);
            _filed = AddLabel(string.Empty);
            _price = AddLabel(string.Empty);
            _files = AddLabel(string.Empty);
            _tasks = AddLabel(string.Empty);
            _options = AddLabel(string.Empty);
            _status = AddLabel(string.Empty);
            _dialog = AddLabel(string.Empty);
            _unknown = AddLabel(string.Empty);

            // 3. Plain English literals, as an existing UI would have them. Nothing here mentions
            //    localization; registering the canvas is enough for the sweep to translate them.
            AddKeyed("section.swept", 18, FontStyle.Bold);
            AddLabel("Settings");
            AddLabel("Profile");
            AddLabel("Messages");
            AddLabel("Help");
            AddLabel("About");
            AddLabel("Sign out");

            // 4. From a static readonly pool: see DemoTips.
            AddKeyed("section.pool", 18, FontStyle.Bold);
            _tip = AddLabel(string.Empty);
            _poolGreeting = AddLabel(string.Empty);

            UITextLocalizer.Enable();
            UITextLocalizer.RegisterRoot(_content.gameObject);
            Refresh();
        }

        private void OnDestroy()
        {
            if (_content != null) UITextLocalizer.UnregisterRoot(_content.gameObject);
            LocalizationService.Reset(); // the service must not keep using settings that are about to go
            if (_settings != null) Destroy(_settings);
        }

        // Everything that code assigns has to be assigned again after a language change. A value
        // that goes into another sentence is fetched with GetRaw so that it is shaped only once.
        private void Refresh()
        {
            if (_content == null) return; // a language change before Start() has built the labels

            int count = Counts[_countIndex];

            _greeting.text = LocalizationService.Get("greeting.welcome", UserName);
            _page.text = LocalizationService.Get("page.number", 7);
            _opened.text = LocalizationService.Get("inbox.opened", LocalizationService.GetRaw("sample.item"));
            _filed.text = LocalizationService.Get("folder.filed", LocalizationService.GetRaw("sample.place"));
            _price.text = LocalizationService.Get("price.label", 250);
            _files.text = LocalizationService.GetPlural("files.count", count);
            _tasks.text = LocalizationService.GetPlural("tasks.open", count);

            // Several texts on one line: join the unshaped pieces, then shape the line once. Joining
            // pieces that were shaped one by one would put them in the wrong order in Arabic.
            _options.text = LocalizationService.ApplyRtl(
                LocalizationService.GetRaw("options.language") + "  |  "
                + LocalizationService.GetRaw("options.sound") + "  |  "
                + LocalizationService.GetRaw("options.notifications"));
            _status.text = LocalizationService.ApplyRtl(
                LocalizationService.GetRaw("status.saving") + "  " + LocalizationService.GetRaw("status.offline"));
            _dialog.text = LocalizationService.ApplyRtl(
                LocalizationService.GetRaw("dialog.quit") + "  [" + LocalizationService.GetRaw("button.ok") + "] ["
                + LocalizationService.GetRaw("button.cancel") + "]");

            // A key that is in no table: the key itself is shown and the gap is reported, not hidden.
            _unknown.text = "(unknown key) " + LocalizationService.Get("demo.not_in_any_table");

            // Pool entries hold text in typing order; shape at the point of display.
            _tip.text = LocalizationService.ApplyRtl(DemoTips.Tips[_tipIndex % DemoTips.Tips.Length]);
            string greeting = DemoTips.Greetings[_tipIndex % DemoTips.Greetings.Length];
            _poolGreeting.text = LocalizationService.ApplyRtl(string.Format(greeting, UserName));

            // Which side text starts on is a layout decision, so it is made here and not in the package.
            TextAnchor anchor = LocalizationService.Engine.IsRtl ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            foreach (Text label in _labels) label.alignment = anchor;

            // The controls in the bar are part of the demo, not of the localized content.
            _countCaption.text = "Count: " + count;
            _fallbacks.text = "Fallbacks reported: " + LocalizationService.Engine.Fallbacks.Count;
            foreach (KeyValuePair<string, Button> pair in _localeButtons)
            {
                pair.Value.interactable = pair.Key != LocalizationService.CurrentLocale;
            }
        }

        // ---- The language picker --------------------------------------------------------------

        private void BuildPicker()
        {
            IReadOnlyList<LocaleInfo> locales = LocalizationService.Catalog.All;
            for (int i = 0; i < locales.Count; i++)
            {
                LocaleInfo locale = locales[i];

                // A picker shows each language in its own script, so the Arabic name needs shaping
                // even while another language is active.
                string caption = locale.IsRtl
                    ? RtlText.Apply(locale.NativeName, BasicArabicShaper.Instance)
                    : locale.NativeName;

                string code = locale.Code;
                Button button = AddBarButton(caption, 0, i / (float)locales.Count, (i + 1) / (float)locales.Count,
                    () => LocalizationService.SetLocale(code), out _);
                _localeButtons[code] = button;
            }

            AddBarButton(string.Empty, 1, 0f, 0.2f, () =>
            {
                _countIndex = (_countIndex + 1) % Counts.Length;
                Refresh();
            }, out _countCaption);

            Text nextTipCaption;
            AddBarButton(string.Empty, 1, 0.2f, 0.5f, () =>
            {
                _tipIndex++;
                Refresh();
            }, out nextTipCaption);
            nextTipCaption.gameObject.AddComponent<LocalizedText>().Key = "button.next_tip";

            _fallbacks = AddText(_bar, string.Empty, 14, FontStyle.Normal);
            _fallbacks.alignment = TextAnchor.MiddleLeft;
            Place((RectTransform)_fallbacks.transform, 1, 0.52f, 1f);
        }

        private Button AddBarButton(string caption, int row, float xMin, float xMax, Action onClick, out Text label)
        {
            var buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)buttonObject.transform;
            rect.SetParent(_bar, false);
            Place(rect, row, xMin, xMax);

            buttonObject.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.6f, 1f);
            Button button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(() => onClick());

            label = AddText(rect, caption, 16, FontStyle.Normal);
            label.alignment = TextAnchor.MiddleCenter;
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return button;
        }

        // Two rows of controls: row 0 on top, row 1 below it, each spanning a share of the width.
        private static void Place(RectTransform rect, int row, float xMin, float xMax)
        {
            float rowHeight = 0.5f;
            rect.anchorMin = new Vector2(xMin, 1f - (row + 1) * rowHeight);
            rect.anchorMax = new Vector2(xMax, 1f - row * rowHeight);
            rect.offsetMin = new Vector2(3f, 3f);
            rect.offsetMax = new Vector2(-3f, -3f);
        }

        // uGUI buttons need an EventSystem with an input module that matches the input backend of
        // the project. A scene that already has one keeps it.
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            var eventSystem = new GameObject("Demo EventSystem", typeof(EventSystem));
#if ENABLE_LEGACY_INPUT_MANAGER
            eventSystem.AddComponent<StandaloneInputModule>();
#else
            // Only the Input System package handles input in this project. Its UI module is looked
            // up by name, so this file compiles whether or not that package is referenced.
            Type moduleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType == null)
            {
                Debug.LogWarning("LanguagePickerDemo: no UI input module was found, so the buttons will not react.");
                return;
            }

            Component module = eventSystem.AddComponent(moduleType);
            moduleType.GetMethod("AssignDefaultActions", Type.EmptyTypes)?.Invoke(module, null);
#endif
        }

        // ---- Canvas construction: nothing below is about localization ---------------------------

        private void BuildCanvas()
        {
            var canvasObject = new GameObject("Demo Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            // Scale with the screen height, so every line fits whatever size the Game view has.
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1000f, 1000f);
            scaler.matchWidthOrHeight = 1f;

            _bar = AddPanel(canvasObject.transform, "Picker");
            _bar.anchorMin = new Vector2(0f, 1f);
            _bar.anchorMax = new Vector2(1f, 1f);
            _bar.pivot = new Vector2(0.5f, 1f);
            _bar.offsetMin = new Vector2(21f, -BarHeight - 8f);
            _bar.offsetMax = new Vector2(-21f, -8f);

            _content = AddPanel(canvasObject.transform, "Content");
            _content.anchorMin = Vector2.zero;
            _content.anchorMax = Vector2.one;
            _content.offsetMin = new Vector2(24f, 16f);
            _content.offsetMax = new Vector2(-24f, -BarHeight - 16f);
        }

        private static RectTransform AddPanel(Transform parent, string panelName)
        {
            var panelObject = new GameObject(panelName, typeof(RectTransform));
            var rect = (RectTransform)panelObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private Text AddLabel(string text, int size = 16, FontStyle style = FontStyle.Normal)
        {
            Text label = AddText(_content, text, size, style);
            var rect = (RectTransform)label.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -_nextY);
            rect.sizeDelta = new Vector2(0f, LineHeight);
            _nextY += LineHeight;
            _labels.Add(label);
            return label;
        }

        private void AddKeyed(string key, int size, FontStyle style)
        {
            Text label = AddLabel(string.Empty, size, style);
            label.gameObject.AddComponent<LocalizedText>().Key = key;
        }

        private Text AddText(Transform parent, string text, int size, FontStyle style)
        {
            var labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(parent, false);

            Text label = labelObject.AddComponent<Text>();
            label.font = _font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = Color.white;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }
    }
}
