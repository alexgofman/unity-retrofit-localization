using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace RetrofitLocalization.Tests
{
    /// <summary>
    /// Edit-mode tests of the label sweep. There is no runner and no timer outside Play mode, so
    /// the tests call <see cref="UITextLocalizer.Sweep"/> where the runner would, and everything
    /// else (registration, exemptions, following a language change) is the code the game runs.
    /// Legacy <see cref="Text"/> labels are used because they need no font asset to hold a string.
    /// </summary>
    public class UITextLocalizerTests
    {
        private RetrofitLocalizationSettings _settings;
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(ServiceFixture.PrefsKey);
            _settings = ServiceFixture.NewSettings();
            ServiceFixture.Start(_settings);
            _root = new GameObject("TestRoot", typeof(RectTransform));
            UITextLocalizer.Enable();
        }

        [TearDown]
        public void TearDown()
        {
            UITextLocalizer.Disable();
            UITextLocalizer.UnregisterRoot(_root);
            if (_root != null) Object.DestroyImmediate(_root);
            ServiceFixture.CleanUp(_settings);
        }

        private Text AddLabel(string text, Transform parent = null)
        {
            var labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(parent != null ? parent : _root.transform, false);
            Text label = labelObject.AddComponent<Text>();
            label.text = text;
            return label;
        }

        [Test]
        public void SourceLanguage_IsInert()
        {
            Text label = AddLabel("Settings");

            UITextLocalizer.RegisterRoot(_root);

            Assert.That(UITextLocalizer.IsActive, Is.False);
            Assert.That(label.text, Is.EqualTo("Settings"));
            Assert.That(UITextLocalizer.TrackedLabelCount, Is.EqualTo(0));
        }

        [Test]
        public void RegisterRoot_TranslatesItsLabels()
        {
            LocalizationService.SetLocale("de");
            Text known = AddLabel("Settings");
            Text unknown = AddLabel("Player 7");

            UITextLocalizer.RegisterRoot(_root);

            Assert.That(known.text, Is.EqualTo("Einstellungen"));
            Assert.That(unknown.text, Is.EqualTo("Player 7"));
        }

        [Test]
        public void LanguageChange_ReachesLabelsThatAreAlreadyOnScreen()
        {
            // The root appears while the source language is active, then the player switches.
            Text label = AddLabel("Settings");
            UITextLocalizer.RegisterRoot(_root);

            LocalizationService.SetLocale("de");

            Assert.That(label.text, Is.EqualTo("Einstellungen"));
        }

        [Test]
        public void SwitchingBetweenTwoTranslations_GoesThroughTheOriginalText()
        {
            Text label = AddLabel("Settings");
            UITextLocalizer.RegisterRoot(_root);

            LocalizationService.SetLocale("de");
            LocalizationService.SetLocale("ru");

            Assert.That(label.text, Is.EqualTo("\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438"));
        }

        [Test]
        public void SwitchingBackToTheSourceLanguage_RestoresTheOriginalText()
        {
            Text label = AddLabel("Settings");
            UITextLocalizer.RegisterRoot(_root);

            LocalizationService.SetLocale("de");
            LocalizationService.SetLocale("en");

            Assert.That(label.text, Is.EqualTo("Settings"));
            Assert.That(UITextLocalizer.TrackedLabelCount, Is.EqualTo(0));
        }

        [Test]
        public void LabelWithoutATranslationInTheNewLanguage_ShowsItsOriginalText()
        {
            // "Continue" is in the German phrase table but not in the Russian one.
            Text label = AddLabel("Continue");
            UITextLocalizer.RegisterRoot(_root);

            LocalizationService.SetLocale("de");
            Assert.That(label.text, Is.EqualTo("Weiter"));

            LocalizationService.SetLocale("ru");
            Assert.That(label.text, Is.EqualTo("Continue"));
        }

        [Test]
        public void TextAssignedByCode_IsTranslatedByTheNextSweep()
        {
            LocalizationService.SetLocale("de");
            Text label = AddLabel("Settings");
            UITextLocalizer.RegisterRoot(_root);

            label.text = "Continue";
            UITextLocalizer.Sweep(_root);

            Assert.That(label.text, Is.EqualTo("Weiter"));
        }

        [Test]
        public void RightToLeftLocale_ShapesTheTranslation()
        {
            Text label = AddLabel("Settings");
            UITextLocalizer.RegisterRoot(_root);

            LocalizationService.SetLocale("ar");

            Assert.That(label.text, Is.EqualTo(BasicArabicShaper.Instance.Shape(Fixtures.Name)));
        }

        [Test]
        public void TypedText_IsNeverTranslated_ButThePlaceholderIs()
        {
            var fieldObject = new GameObject("Field", typeof(RectTransform));
            fieldObject.transform.SetParent(_root.transform, false);
            InputField field = fieldObject.AddComponent<InputField>();
            Text typed = AddLabel(string.Empty, fieldObject.transform);
            Text placeholder = AddLabel("Continue", fieldObject.transform);
            field.textComponent = typed;
            field.placeholder = placeholder;
            // The player typed a word that happens to be a phrase in the table.
            field.text = "Settings";
            typed.text = "Settings";

            UITextLocalizer.RegisterRoot(_root);
            LocalizationService.SetLocale("de");

            Assert.That(typed.text, Is.EqualTo("Settings"));
            Assert.That(field.text, Is.EqualTo("Settings"));
            Assert.That(placeholder.text, Is.EqualTo("Weiter"));
        }

        [Test]
        public void ExemptLabel_IsLeftAlone()
        {
            Text label = AddLabel("Settings");
            UITextLocalizer.Exempt(label);
            UITextLocalizer.RegisterRoot(_root);

            LocalizationService.SetLocale("de");

            Assert.That(label.text, Is.EqualTo("Settings"));
        }

        [Test]
        public void ExemptingATranslatedLabel_PutsItsOriginalTextBack()
        {
            Text label = AddLabel("Settings");
            UITextLocalizer.RegisterRoot(_root);
            LocalizationService.SetLocale("de");
            Assert.That(label.text, Is.EqualTo("Einstellungen"));

            UITextLocalizer.Exempt(label);
            UITextLocalizer.Sweep(_root);

            Assert.That(label.text, Is.EqualTo("Settings"));
        }

        [Test]
        public void Exempt_KeepsTextThatCodeAssignedAfterTheLastSweep()
        {
            Text label = AddLabel("Settings");
            UITextLocalizer.RegisterRoot(_root);
            LocalizationService.SetLocale("de");
            label.text = "42"; // the game reuses the label for a value, then protects it

            UITextLocalizer.Exempt(label);

            Assert.That(label.text, Is.EqualTo("42"));
        }

        [Test]
        public void LabelThatBecomesTypedText_IsLeftAloneFromThenOn()
        {
            LocalizationService.SetLocale("de");
            var fieldObject = new GameObject("Field", typeof(RectTransform));
            fieldObject.transform.SetParent(_root.transform, false);
            InputField field = fieldObject.AddComponent<InputField>();
            Text label = AddLabel("Continue", fieldObject.transform);
            UITextLocalizer.RegisterRoot(_root);
            Assert.That(label.text, Is.EqualTo("Weiter"));

            // Only now does the label become the text of the input field, and the player types.
            field.textComponent = label;
            field.text = "Settings";
            label.text = "Settings";
            UITextLocalizer.Sweep(_root);

            Assert.That(label.text, Is.EqualTo("Settings"));
        }

        [Test]
        public void ServiceRestartedWithoutAnEvent_IsNoticedByTheNextSweep()
        {
            Text label = AddLabel("Settings");
            UITextLocalizer.RegisterRoot(_root);
            LocalizationService.SetLocale("de");
            Assert.That(label.text, Is.EqualTo("Einstellungen"));

            // Start over in the source language. Configure raises no event.
            LocalizationService.ClearSavedLocale();
            ServiceFixture.Start(_settings);
            UITextLocalizer.SweepAll();

            Assert.That(label.text, Is.EqualTo("Settings"));
            Assert.That(UITextLocalizer.TrackedLabelCount, Is.EqualTo(0));
        }

        [Test]
        public void Reload_RetranslatesLabelsFromTheNewTables()
        {
            MemoryTextSource files = Fixtures.Files();
            ServiceFixture.Start(_settings, files);
            Text label = AddLabel("Settings");
            UITextLocalizer.RegisterRoot(_root);
            LocalizationService.SetLocale("de");
            Assert.That(label.text, Is.EqualTo("Einstellungen"));

            files.With("Localization/phrases_de", "{ \"Settings\": \"Optionen\" }");
            LocalizationService.Reload();

            Assert.That(label.text, Is.EqualTo("Optionen"));
        }

        [Test]
        public void KeyDrivenLabel_IsNotSwept()
        {
            Text label = AddLabel("Settings");
            label.gameObject.AddComponent<LocalizedText>();
            UITextLocalizer.RegisterRoot(_root);

            LocalizationService.SetLocale("de");

            // The phrase table has "Settings", but this label belongs to its LocalizedText component.
            Assert.That(label.text, Is.EqualTo("Settings"));
        }

        [Test]
        public void LocalizedText_ShowsItsKeyInTheActiveLanguage()
        {
            LocalizationService.SetLocale("de");
            Text label = AddLabel(string.Empty);
            LocalizedText localized = label.gameObject.AddComponent<LocalizedText>();

            localized.Key = "menu.play";
            Assert.That(label.text, Is.EqualTo("Spielen"));

            LocalizationService.SetLocale("ru");
            localized.Apply();
            Assert.That(label.text, Is.EqualTo("\u0418\u0433\u0440\u0430\u0442\u044C"));
        }

        [Test]
        public void Disable_PutsEveryLabelBack()
        {
            Text label = AddLabel("Settings");
            UITextLocalizer.RegisterRoot(_root);
            LocalizationService.SetLocale("de");

            UITextLocalizer.Disable();

            Assert.That(label.text, Is.EqualTo("Settings"));
            Assert.That(UITextLocalizer.TrackedLabelCount, Is.EqualTo(0));
        }

        [Test]
        public void DestroyedLabels_AreForgotten()
        {
            Text first = AddLabel("Settings");
            AddLabel("Continue");
            UITextLocalizer.RegisterRoot(_root);
            LocalizationService.SetLocale("de");
            Assert.That(UITextLocalizer.TrackedLabelCount, Is.EqualTo(2));

            Object.DestroyImmediate(first.gameObject);
            UITextLocalizer.SweepAll();

            Assert.That(UITextLocalizer.TrackedLabelCount, Is.EqualTo(1));
        }

        [Test]
        public void UnregisteredRoot_IsNoLongerSwept()
        {
            Text label = AddLabel("Settings");
            UITextLocalizer.RegisterRoot(_root);
            UITextLocalizer.UnregisterRoot(_root);

            LocalizationService.SetLocale("de");

            Assert.That(label.text, Is.EqualTo("Settings"));
        }
    }
}
