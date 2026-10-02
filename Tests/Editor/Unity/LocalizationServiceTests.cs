using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RetrofitLocalization.Tests
{
    /// <summary>Settings and clean-up shared by the tests that go through the static facade.</summary>
    internal static class ServiceFixture
    {
        // A key of its own, so the tests never touch the saved choice of the project they run in.
        public const string PrefsKey = "retrofit_localization.tests.locale";

        public static RetrofitLocalizationSettings NewSettings()
        {
            var settings = ScriptableObject.CreateInstance<RetrofitLocalizationSettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            settings.PlayerPrefsKey = PrefsKey;
            settings.LogFallbacks = false;
            // Never sweep the canvases of whatever scene happens to be open in the Editor.
            settings.UiSweepMode = UiSweepMode.RegisteredRoots;

            settings.Locales.Clear();
            settings.Locales.Add(new RetrofitLocalizationSettings.Locale("en", "English", "English", false, SystemLanguage.English));
            settings.Locales.Add(new RetrofitLocalizationSettings.Locale("de", "Deutsch", "German", false, SystemLanguage.German));
            settings.Locales.Add(new RetrofitLocalizationSettings.Locale("ru", "Russian", "Russian", false, SystemLanguage.Russian));
            settings.Locales.Add(new RetrofitLocalizationSettings.Locale("ko", "Korean", "Korean", false, SystemLanguage.Korean));
            settings.Locales.Add(new RetrofitLocalizationSettings.Locale("ar", "Arabic", "Arabic", true, SystemLanguage.Arabic));
            settings.Locales.Add(new RetrofitLocalizationSettings.Locale("fr", "French", "French", false, SystemLanguage.French));
            return settings;
        }

        public static void Start(RetrofitLocalizationSettings settings, ITextSource files = null)
        {
            LocalizationService.Configure(settings, files ?? Fixtures.Files());
        }

        public static void CleanUp(RetrofitLocalizationSettings settings)
        {
            LocalizationService.Reset();
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
            if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
        }
    }

    /// <summary>
    /// Table files that can be made to fail the way the engine API does when it is called from a
    /// constructor, a field initializer or a loading thread.
    /// </summary>
    internal sealed class GatedTextSource : ITextSource
    {
        private readonly ITextSource _files;

        public GatedTextSource(ITextSource files = null)
        {
            _files = files ?? Fixtures.Files();
        }

        public bool Blocked { get; set; }

        public string Load(string path)
        {
            if (Blocked) throw new UnityException("Load is not allowed to be called from here.");
            return _files.Load(path);
        }
    }

    public class LocalizationServiceTests
    {
        private RetrofitLocalizationSettings _settings;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(ServiceFixture.PrefsKey);
            _settings = ServiceFixture.NewSettings();
            ServiceFixture.Start(_settings);
        }

        [TearDown]
        public void TearDown()
        {
            ServiceFixture.CleanUp(_settings);
        }

        [Test]
        public void WithoutASavedChoice_StartsInTheSourceLanguage()
        {
            Assert.That(LocalizationService.CurrentLocale, Is.EqualTo("en"));
            Assert.That(LocalizationService.Get("menu.play"), Is.EqualTo("Play"));
        }

        [Test]
        public void SavedChoice_IsUsedAtStartUp()
        {
            PlayerPrefs.SetString(ServiceFixture.PrefsKey, "de");
            ServiceFixture.Start(_settings);

            Assert.That(LocalizationService.CurrentLocale, Is.EqualTo("de"));
            Assert.That(LocalizationService.Get("menu.play"), Is.EqualTo("Spielen"));
        }

        [Test]
        public void SavedChoiceThatLeftTheCatalog_FallsBackToTheSourceLanguage()
        {
            PlayerPrefs.SetString(ServiceFixture.PrefsKey, "xx");
            ServiceFixture.Start(_settings);

            Assert.That(LocalizationService.CurrentLocale, Is.EqualTo("en"));
        }

        [Test]
        public void SetLocale_SavesTheChoice_AndRaisesTheEventOnce()
        {
            int changes = 0;
            Action handler = () => changes++;
            LocalizationService.OnLanguageChanged += handler;
            try
            {
                bool accepted = LocalizationService.SetLocale("de");

                Assert.That(accepted, Is.True);
                Assert.That(changes, Is.EqualTo(1));
                Assert.That(LocalizationService.CurrentLocale, Is.EqualTo("de"));
                Assert.That(PlayerPrefs.GetString(ServiceFixture.PrefsKey, string.Empty), Is.EqualTo("de"));
            }
            finally
            {
                LocalizationService.OnLanguageChanged -= handler;
            }
        }

        [Test]
        public void SetLocale_WithAnUnknownCode_ChangesAndSavesNothing()
        {
            bool accepted = LocalizationService.SetLocale("xx");

            Assert.That(accepted, Is.False);
            Assert.That(LocalizationService.CurrentLocale, Is.EqualTo("en"));
            Assert.That(PlayerPrefs.HasKey(ServiceFixture.PrefsKey), Is.False);
        }

        [Test]
        public void ClearSavedLocale_KeepsTheActiveLanguage()
        {
            LocalizationService.SetLocale("de");

            LocalizationService.ClearSavedLocale();

            Assert.That(PlayerPrefs.HasKey(ServiceFixture.PrefsKey), Is.False);
            Assert.That(LocalizationService.CurrentLocale, Is.EqualTo("de"));
        }

        [Test]
        public void MissingKey_IsRaisedThroughOnFallback()
        {
            FallbackReport? seen = null;
            Action<FallbackReport> handler = report => seen = report;
            LocalizationService.OnFallback += handler;
            try
            {
                LocalizationService.SetLocale("de");

                Assert.That(LocalizationService.Get("menu.quit"), Is.EqualTo("Quit"));
                Assert.That(seen.HasValue, Is.True);
                Assert.That(seen.Value.Kind, Is.EqualTo(FallbackKind.MissingKey));
                Assert.That(seen.Value.Key, Is.EqualTo("menu.quit"));
            }
            finally
            {
                LocalizationService.OnFallback -= handler;
            }
        }

        [Test]
        public void Pool_FollowsTheLanguage_AndComesBackUnchanged()
        {
            string[] pool = LocalizationService.LocalizePool("Tips", "loading", new[] { "Tip one", "Tip two", "Tip three" });

            LocalizationService.SetLocale("de");
            Assert.That(pool, Is.EqualTo(new[] { "Tipp eins", "Tipp zwei", "Tipp drei" }));

            LocalizationService.SetLocale("en");
            Assert.That(pool, Is.EqualTo(new[] { "Tip one", "Tip two", "Tip three" }));
        }

        [Test]
        public void Pool_RegisteredInAnEarlierSession_IsBroughtInLineByTheNextStart()
        {
            // What happens when the Editor enters Play mode without a domain reload: the static
            // field still holds the array from the last session, and the service starts afresh.
            string[] pool = LocalizationService.LocalizePool("Tips", "loading", new[] { "Tip one", "Tip two", "Tip three" });
            LocalizationService.SetLocale("de");
            LocalizationService.ClearSavedLocale();

            ServiceFixture.Start(_settings); // a new session, starting in the source language
            LocalizationService.Init();

            Assert.That(pool, Is.EqualTo(new[] { "Tip one", "Tip two", "Tip three" }));
        }

        [Test]
        public void Pool_RegisteredBeforeTheServiceCanStart_IsLocalizedByTheNextLookup()
        {
            // The static initializer of a pool owned by a component can run while the engine is
            // constructing that component, where the tables cannot be read.
            var files = new GatedTextSource();
            PlayerPrefs.SetString(ServiceFixture.PrefsKey, "de");
            ServiceFixture.Start(_settings, files);

            files.Blocked = true;
            string[] pool = LocalizationService.LocalizePool("Tips", "loading", new[] { "Tip one", "Tip two", "Tip three" });

            Assert.That(pool, Is.EqualTo(new[] { "Tip one", "Tip two", "Tip three" }));
            Assert.That(LocalizationService.IsInitialized, Is.False);

            files.Blocked = false;
            Assert.That(LocalizationService.Get("menu.play"), Is.EqualTo("Spielen"));
            Assert.That(pool, Is.EqualTo(new[] { "Tipp eins", "Tipp zwei", "Tipp drei" }));
        }

        [Test]
        public void Pool_RegisteredWhereItsTableCannotBeRead_IsLocalizedByTheNextLookup()
        {
            // A pool table of its own, so no pool of another test has made the service read it yet.
            var files = new GatedTextSource(Fixtures.Files()
                .With("Localization/Pools/Late_de", "{ \"lines\": [\"Eins\", \"Zwei\"] }"));
            ServiceFixture.Start(_settings, files);
            LocalizationService.SetLocale("de");

            files.Blocked = true;
            string[] pool = LocalizationService.LocalizePool("Late", "lines", new[] { "One", "Two" });
            Assert.That(pool, Is.EqualTo(new[] { "One", "Two" }));

            files.Blocked = false;
            LocalizationService.Get("menu.play");
            Assert.That(pool, Is.EqualTo(new[] { "Eins", "Zwei" }));
        }

        [Test]
        public void Reload_ReadsTheTablesAgain_AndRaisesTheEvent()
        {
            MemoryTextSource files = Fixtures.Files();
            ServiceFixture.Start(_settings, files);
            LocalizationService.SetLocale("de");
            int changes = 0;
            Action handler = () => changes++;
            LocalizationService.OnLanguageChanged += handler;
            try
            {
                files.With("Localization/de", "{ \"menu.play\": \"Los\" }");

                LocalizationService.Reload();

                Assert.That(LocalizationService.Get("menu.play"), Is.EqualTo("Los"));
                Assert.That(changes, Is.EqualTo(1));
            }
            finally
            {
                LocalizationService.OnLanguageChanged -= handler;
            }
        }

        [Test]
        public void FailingSubscriber_IsLogged_AndDoesNotStopTheOthers()
        {
            int reached = 0;
            Action failing = () => throw new InvalidOperationException("subscriber failed");
            Action following = () => reached++;
            LocalizationService.OnLanguageChanged += failing;
            LocalizationService.OnLanguageChanged += following;
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("subscriber failed"));

                LocalizationService.SetLocale("de");

                Assert.That(reached, Is.EqualTo(1));
                Assert.That(LocalizationService.CurrentLocale, Is.EqualTo("de"));
            }
            finally
            {
                LocalizationService.OnLanguageChanged -= failing;
                LocalizationService.OnLanguageChanged -= following;
            }
        }

        [Test]
        public void SettingsCatalog_SkipsUnusableEntries_AndAlwaysContainsTheSourceLocale()
        {
            RetrofitLocalizationSettings settings = ServiceFixture.NewSettings();
            try
            {
                settings.Locales.Clear();
                settings.Locales.Add(new RetrofitLocalizationSettings.Locale("de", "Deutsch", "German"));
                settings.Locales.Add(new RetrofitLocalizationSettings.Locale("de", "Deutsch", "German"));
                settings.Locales.Add(new RetrofitLocalizationSettings.Locale(string.Empty, "Nameless", "Nameless"));

                // Each skipped entry is logged as a warning, which does not fail a test.
                LocaleCatalog catalog = settings.BuildCatalog();

                Assert.That(catalog.SourceLocale, Is.EqualTo("en"));
                Assert.That(catalog.Contains("en"), Is.True);
                Assert.That(catalog.TargetLocales, Is.EqualTo(new[] { "de" }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }
    }
}
