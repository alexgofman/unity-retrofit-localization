using System.Collections.Generic;
using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    public class LocalizerTests
    {
        private Localizer _localizer;
        private List<FallbackReport> _reports;

        [SetUp]
        public void SetUp()
        {
            _localizer = Fixtures.Localizer();
            _reports = new List<FallbackReport>();
            _localizer.FallbackReported += _reports.Add;
        }

        private void AssertReported(FallbackKind kind, string key)
        {
            foreach (FallbackReport report in _reports)
            {
                if (report.Kind == kind && report.Key == key) return;
            }

            Assert.Fail("Expected a " + kind + " report for '" + key + "', got: " + string.Join("; ", _reports));
        }

        // ---- Plain lookup ---------------------------------------------------------------------

        [Test]
        public void Get_ReturnsTheSourceLanguage_BeforeAnyLocaleIsSet()
        {
            Assert.That(_localizer.CurrentLocale, Is.EqualTo("en"));
            Assert.That(_localizer.Get("menu.play"), Is.EqualTo("Play"));
            Assert.That(_reports, Is.Empty);
        }

        [Test]
        public void Get_ReturnsTheActiveLocale()
        {
            _localizer.SetLocale("de");

            Assert.That(_localizer.Get("menu.play"), Is.EqualTo("Spielen"));
            Assert.That(_reports, Is.Empty);
        }

        [Test]
        public void Get_WithArguments_FillsTheSlots()
        {
            _localizer.SetLocale("de");

            Assert.That(_localizer.Get("greeting", "Tester"), Is.EqualTo("Hallo, Tester!"));
            Assert.That(_localizer.Get("score.line", "Tester", 12), Is.EqualTo("Tester hat 12 Punkte erzielt"));
        }

        // ---- Fallbacks are reported, never silent ---------------------------------------------

        [Test]
        public void MissingKey_FallsBackToTheSourceLanguage_AndIsReported()
        {
            _localizer.SetLocale("de");

            Assert.That(_localizer.Get("menu.quit"), Is.EqualTo("Quit"));

            Assert.That(_reports.Count, Is.EqualTo(1));
            Assert.That(_reports[0].Kind, Is.EqualTo(FallbackKind.MissingKey));
            Assert.That(_reports[0].Locale, Is.EqualTo("de"));
            Assert.That(_reports[0].Key, Is.EqualTo("menu.quit"));
        }

        [Test]
        public void UnknownKey_ReturnsTheKeyItself_AndIsReported()
        {
            _localizer.SetLocale("de");

            Assert.That(_localizer.Get("no.such.key"), Is.EqualTo("no.such.key"));

            AssertReported(FallbackKind.UnknownKey, "no.such.key");
        }

        [Test]
        public void UnknownKey_IsReportedInTheSourceLanguageToo()
        {
            Assert.That(_localizer.Get("no.such.key"), Is.EqualTo("no.such.key"));
            AssertReported(FallbackKind.UnknownKey, "no.such.key");
        }

        [Test]
        public void EachFallback_IsReportedOncePerLocale()
        {
            _localizer.SetLocale("de");
            _localizer.Get("menu.quit");
            _localizer.Get("menu.quit");
            _localizer.Get("menu.quit");
            Assert.That(_reports.Count, Is.EqualTo(1));

            // Activating a locale starts a fresh log, so the same gap is reported again.
            _localizer.SetLocale("ru");
            _localizer.Get("menu.quit");
            Assert.That(_reports.Count, Is.EqualTo(2));
            Assert.That(_reports[1].Locale, Is.EqualTo("ru"));
        }

        [Test]
        public void Fallbacks_AreAlsoKeptAsAList()
        {
            _localizer.SetLocale("de");
            _localizer.Get("menu.quit");
            _localizer.Get("no.such.key");

            Assert.That(_localizer.Fallbacks.Count, Is.EqualTo(2));
        }

        [Test]
        public void MissingTableFile_IsReported_AndEveryKeyFallsBack()
        {
            _localizer.SetLocale("fr");

            AssertReported(FallbackKind.MissingTable, "Localization/fr");
            Assert.That(_localizer.Get("menu.play"), Is.EqualTo("Play"));
            AssertReported(FallbackKind.MissingKey, "menu.play");
        }

        [Test]
        public void MalformedTableFile_IsReported_AndTreatedAsEmpty()
        {
            MemoryTextSource files = Fixtures.Files().With("Localization/de", "{ \"menu.play\": 5 }");
            var localizer = new Localizer(Fixtures.Catalog(), files);
            localizer.FallbackReported += _reports.Add;

            localizer.SetLocale("de");

            AssertReported(FallbackKind.MalformedTable, "Localization/de");
            Assert.That(localizer.Get("menu.play"), Is.EqualTo("Play"));
        }

        [Test]
        public void FormatError_ReturnsTheUnformattedText_AndIsReported()
        {
            // The German text asks for {3}, a slot the call does not supply.
            _localizer.SetLocale("de");

            string text = _localizer.Get("filed.under", "Inbox");

            Assert.That(text, Is.EqualTo("Unter {0} abgelegt, Platz {3}"));
            AssertReported(FallbackKind.FormatError, "filed.under");
        }

        [Test]
        public void GetOr_UsesTheDefaultTextOnlyWhenTheKeyIsNowhere()
        {
            _localizer.SetLocale("de");

            Assert.That(_localizer.GetOr("menu.play", "fallback"), Is.EqualTo("Spielen"));
            Assert.That(_localizer.GetOr("menu.quit", "fallback"), Is.EqualTo("Quit"));
            Assert.That(_localizer.GetOr("no.such.key", "fallback"), Is.EqualTo("fallback"));

            AssertReported(FallbackKind.MissingKey, "menu.quit");
            AssertReported(FallbackKind.UnknownKey, "no.such.key");
        }

        [Test]
        public void Has_IsTrueForKeysInEitherTable()
        {
            _localizer.SetLocale("de");

            Assert.That(_localizer.Has("menu.play"), Is.True);
            Assert.That(_localizer.Has("menu.quit"), Is.True);
            Assert.That(_localizer.Has("no.such.key"), Is.False);
            Assert.That(_localizer.Has(null), Is.False);
            Assert.That(_reports, Is.Empty);
        }

        // ---- Locale switching -----------------------------------------------------------------

        [Test]
        public void SetLocale_WithAnUnknownCode_ChangesNothing_AndIsReported()
        {
            _localizer.SetLocale("de");
            int changes = 0;
            _localizer.LanguageChanged += () => changes++;

            bool accepted = _localizer.SetLocale("xx");

            Assert.That(accepted, Is.False);
            Assert.That(_localizer.CurrentLocale, Is.EqualTo("de"));
            Assert.That(changes, Is.EqualTo(0));
            AssertReported(FallbackKind.UnknownLocale, "xx");
        }

        [Test]
        public void SetLocale_RaisesLanguageChangedOnce_LoadDoesNot()
        {
            int changes = 0;
            _localizer.LanguageChanged += () => changes++;

            _localizer.Load("de");
            Assert.That(changes, Is.EqualTo(0));

            _localizer.SetLocale("ru");
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(_localizer.CurrentLocale, Is.EqualTo("ru"));
        }

        [Test]
        public void SwitchingBackToTheSourceLanguage_RestoresSourceText()
        {
            _localizer.SetLocale("de");
            _localizer.SetLocale("en");

            Assert.That(_localizer.IsSourceLocaleActive, Is.True);
            Assert.That(_localizer.Get("menu.play"), Is.EqualTo("Play"));
        }

        // ---- Plurals --------------------------------------------------------------------------

        [TestCase(1, "1 apple")]
        [TestCase(2, "2 apples")]
        [TestCase(0, "0 apples")]
        public void GetPlural_English(long count, string expected)
        {
            Assert.That(_localizer.GetPlural("apples", count), Is.EqualTo(expected));
        }

        [TestCase(1, "1 \u044F\u0431\u043B\u043E\u043A\u043E")]
        [TestCase(3, "3 \u044F\u0431\u043B\u043E\u043A\u0430")]
        [TestCase(5, "5 \u044F\u0431\u043B\u043E\u043A")]
        [TestCase(21, "21 \u044F\u0431\u043B\u043E\u043A\u043E")]
        [TestCase(11, "11 \u044F\u0431\u043B\u043E\u043A")]
        public void GetPlural_Russian(long count, string expected)
        {
            _localizer.SetLocale("ru");
            Assert.That(_localizer.GetPlural("apples", count), Is.EqualTo(expected));
            Assert.That(_reports, Is.Empty);
        }

        [Test]
        public void GetPlural_LanguageWithoutPlural_UsesTheOtherForm()
        {
            _localizer.SetLocale("ko");
            Assert.That(_localizer.GetPlural("apples", 1), Is.EqualTo("\uC0AC\uACFC 1\uAC1C"));
            Assert.That(_localizer.GetPlural("apples", 7), Is.EqualTo("\uC0AC\uACFC 7\uAC1C"));
        }

        [Test]
        public void GetPlural_MissingForm_UsesOther_AndIsReported()
        {
            MemoryTextSource files = Fixtures.Files().With("Localization/ru",
                "{ \"apples.one\": \"{0} A\", \"apples.other\": \"{0} B\" }");
            var localizer = new Localizer(Fixtures.Catalog(), files);
            localizer.FallbackReported += _reports.Add;
            localizer.SetLocale("ru");

            Assert.That(localizer.GetPlural("apples", 5), Is.EqualTo("5 B"));
            AssertReported(FallbackKind.MissingPluralForm, "apples.many");
        }

        [Test]
        public void GetPlural_MissingInTheLocale_UsesTheSourceLanguagesOwnRule()
        {
            // French has no table at all. Zero is singular in French but plural in English, and the
            // text that is shown is English, so the English rule has to pick the form.
            _localizer.SetLocale("fr");

            Assert.That(_localizer.GetPlural("apples", 0), Is.EqualTo("0 apples"));
            Assert.That(_localizer.GetPlural("apples", 1), Is.EqualTo("1 apple"));
            AssertReported(FallbackKind.MissingKey, "apples");
        }

        [Test]
        public void GetPlural_PassesFurtherArgumentsAfterTheCount()
        {
            MemoryTextSource files = Fixtures.Files().With("Localization/en",
                "{ \"gift.one\": \"{1} sent {0} gift\", \"gift.other\": \"{1} sent {0} gifts\" }");
            var localizer = new Localizer(Fixtures.Catalog(), files);

            Assert.That(localizer.GetPlural("gift", 3, "Tester"), Is.EqualTo("Tester sent 3 gifts"));
        }

        // ---- Korean particles -----------------------------------------------------------------

        [Test]
        public void KoreanLocale_ResolvesParticlesAfterFormatting()
        {
            _localizer.SetLocale("ko");

            // The inserted noun decides the particle: consonant ending, then vowel ending.
            Assert.That(_localizer.Get("item.picked", "\uAC80"),
                Is.EqualTo("\uAC80\uC744 \uC8FC\uC6E0\uC2B5\uB2C8\uB2E4"));
            Assert.That(_localizer.Get("item.picked", "\uC0AC\uACFC"),
                Is.EqualTo("\uC0AC\uACFC\uB97C \uC8FC\uC6E0\uC2B5\uB2C8\uB2E4"));
            // Rieul ending with the direction particle.
            Assert.That(_localizer.Get("filed.under", "\uC11C\uC6B8"), Is.EqualTo("\uC11C\uC6B8\uB85C \uBD84\uB958"));
        }

        [Test]
        public void ParticleResolution_CanBeSwitchedOff()
        {
            var options = new LocalizerOptions { ResolveKoreanParticles = false };
            var localizer = new Localizer(Fixtures.Catalog(), Fixtures.Files(), options);
            localizer.SetLocale("ko");

            Assert.That(localizer.Get("item.picked", "\uAC80"),
                Is.EqualTo("\uAC80\uC744(\uB97C) \uC8FC\uC6E0\uC2B5\uB2C8\uB2E4"));
        }

        // ---- Phrases --------------------------------------------------------------------------

        [Test]
        public void Translate_LooksUpSourceText()
        {
            _localizer.SetLocale("de");

            Assert.That(_localizer.Translate("Settings"), Is.EqualTo("Einstellungen"));
            Assert.That(_localizer.Translate("  Settings\n"), Is.EqualTo("  Einstellungen\n"));
            Assert.That(_reports, Is.Empty);
        }

        [Test]
        public void Translate_Miss_ReturnsTheInput_AndIsReported()
        {
            _localizer.SetLocale("de");

            Assert.That(_localizer.Translate("New Game"), Is.EqualTo("New Game"));
            AssertReported(FallbackKind.MissingPhrase, "New Game");
        }

        [Test]
        public void Translate_InTheSourceLanguage_IsAPassThrough()
        {
            Assert.That(_localizer.Translate("Settings"), Is.EqualTo("Settings"));
            Assert.That(_reports, Is.Empty);
        }

        [Test]
        public void TryTranslate_IsQuietOnAMiss()
        {
            _localizer.SetLocale("de");

            Assert.That(_localizer.TryTranslate("Player 7", out string translated), Is.False);
            Assert.That(translated, Is.Null);
            Assert.That(_localizer.TryTranslate("Continue", out translated), Is.True);
            Assert.That(translated, Is.EqualTo("Weiter"));
            Assert.That(_reports, Is.Empty);
        }

        [Test]
        public void HasPhrases_TellsWhetherASweepIsWorthRunning()
        {
            Assert.That(_localizer.HasPhrases, Is.False);
            _localizer.SetLocale("de");
            Assert.That(_localizer.HasPhrases, Is.True);
            _localizer.SetLocale("ko");
            Assert.That(_localizer.HasPhrases, Is.False);
        }

        // ---- Right-to-left --------------------------------------------------------------------

        [Test]
        public void RtlLocale_ShapesLookups_ButNotTheRawVariants()
        {
            var shaper = new BracketShaper();
            _localizer.RtlShaper = shaper;
            _localizer.SetLocale("ar");

            Assert.That(_localizer.IsRtl, Is.True);
            Assert.That(_localizer.Get("menu.play"), Is.EqualTo("[" + Fixtures.Text + "]"));
            Assert.That(_localizer.GetRaw("menu.play"), Is.EqualTo(Fixtures.Text));
            Assert.That(_localizer.Translate("Settings"), Is.EqualTo("[" + Fixtures.Name + "]"));
            Assert.That(_localizer.TranslateRaw("Settings"), Is.EqualTo(Fixtures.Name));
        }

        [Test]
        public void RtlLocale_ShapesAfterFormatting_AndKeepsTags()
        {
            var shaper = new BracketShaper();
            _localizer.RtlShaper = shaper;
            _localizer.SetLocale("ar");

            // Source: "<color=#FFD54A>{0}</color> <gold>". The number stays as it is, the Arabic run
            // is shaped, the runs are emitted right to left and the tags still wrap the number.
            string text = _localizer.Get("price.tag", 250);

            Assert.That(text, Is.EqualTo("[ " + Fixtures.Gold + "]<color=#FFD54A>250</color>"));
            Assert.That(shaper.Runs, Is.EqualTo(new[] { " " + Fixtures.Gold }));
        }

        [Test]
        public void RtlLocale_LeavesSourceLanguageFallbacksAlone()
        {
            _localizer.RtlShaper = new BracketShaper();
            _localizer.SetLocale("ar");

            Assert.That(_localizer.Get("menu.quit"), Is.EqualTo("Quit"));
        }

        [Test]
        public void LeftToRightLocale_NeverCallsTheShaper()
        {
            var shaper = new BracketShaper();
            _localizer.RtlShaper = shaper;
            _localizer.SetLocale("de");

            Assert.That(_localizer.ApplyRtl(Fixtures.Hello), Is.EqualTo(Fixtures.Hello));
            Assert.That(shaper.Runs, Is.Empty);
        }
    }
}
