using System.Collections.Generic;
using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    public class TableAuditTests
    {
        private static StringTable Table(string locale, params string[] keysAndValues)
        {
            var entries = new Dictionary<string, string>();
            for (int i = 0; i < keysAndValues.Length; i += 2) entries[keysAndValues[i]] = keysAndValues[i + 1];
            return new StringTable(locale, entries);
        }

        private static readonly StringTable English = Table("en",
            "menu.play", "Play",
            "greeting", "Hello, {0}!",
            "score", "{0} against {1}",
            "days.one", "{0} day",
            "days.other", "{0} days");

        private static List<string> Describe(List<TableProblem> problems)
        {
            var lines = new List<string>();
            foreach (TableProblem problem in problems) lines.Add(problem.Kind + ":" + problem.Key);
            return lines;
        }

        [Test]
        public void CompleteTranslation_HasNoProblems()
        {
            StringTable german = Table("de",
                "menu.play", "Spielen",
                "greeting", "Hallo, {0}!",
                "score", "{0} gegen {1}",
                "days.one", "{0} Tag",
                "days.other", "{0} Tage");

            Assert.That(TableAudit.Compare(English, german), Is.Empty);
        }

        [Test]
        public void MissingAndExtraKeys_AreReported()
        {
            StringTable german = Table("de",
                "greeting", "Hallo, {0}!",
                "score", "{0} gegen {1}",
                "days.one", "{0} Tag",
                "days.other", "{0} Tage",
                "menu.paly", "Spielen"); // a typo in the key

            Assert.That(Describe(TableAudit.Compare(English, german)),
                Is.EqualTo(new[] { "MissingKey:menu.play", "ExtraKey:menu.paly" }));
        }

        [Test]
        public void BlankTranslation_IsReported()
        {
            StringTable german = Table("de",
                "menu.play", "",
                "greeting", "Hallo, {0}!",
                "score", "{0} gegen {1}",
                "days.one", "{0} Tag",
                "days.other", "{0} Tage");

            Assert.That(Describe(TableAudit.Compare(English, german)), Is.EqualTo(new[] { "EmptyValue:menu.play" }));
        }

        [Test]
        public void SlotDrift_IsReported()
        {
            StringTable german = Table("de",
                "menu.play", "Spielen",
                "greeting", "Hallo, {0} und {1}!", // {1} does not exist in the source
                "score", "nur {1}",                 // {0} was lost
                "days.one", "{0} Tag",
                "days.other", "{0} Tage");

            List<TableProblem> problems = TableAudit.Compare(English, german);

            Assert.That(Describe(problems), Is.EqualTo(new[] { "ExtraPlaceholder:greeting", "DroppedPlaceholder:score" }));
            Assert.That(problems[0].Detail, Is.EqualTo("{1}"));
            Assert.That(problems[1].Detail, Is.EqualTo("{0}"));
        }

        [Test]
        public void BrokenBraces_AreReported()
        {
            StringTable german = Table("de",
                "menu.play", "Spielen",
                "greeting", "Hallo, {0!",
                "score", "{0} gegen {1}",
                "days.one", "{0} Tag",
                "days.other", "{0} Tage");

            List<string> problems = Describe(TableAudit.Compare(English, german));

            Assert.That(problems, Does.Contain("MalformedPlaceholder:greeting"));
        }

        [Test]
        public void DoublyEscapedLineBreak_IsReported()
        {
            StringTable german = Table("de",
                "menu.play", "Spielen\\nJetzt", // backslash + n, not a line break
                "greeting", "Hallo, {0}!",
                "score", "{0} gegen {1}",
                "days.one", "{0} Tag",
                "days.other", "{0} Tage");

            Assert.That(Describe(TableAudit.Compare(English, german)), Is.EqualTo(new[] { "EscapedLineBreak:menu.play" }));
        }

        [Test]
        public void PluralForms_AreCheckedAgainstTheGrammarOfTheTranslation()
        {
            // Russian needs one, few and many (plus other). Forms English never has are not "extra".
            StringTable russian = Table("ru",
                "menu.play", "x",
                "greeting", "{0}",
                "score", "{0} {1}",
                "days.one", "{0} a",
                "days.few", "{0} b",
                "days.many", "{0} c",
                "days.other", "{0} d");

            Assert.That(TableAudit.Compare(English, russian), Is.Empty);
        }

        [Test]
        public void MissingPluralForm_IsReported()
        {
            StringTable russian = Table("ru",
                "menu.play", "x",
                "greeting", "{0}",
                "score", "{0} {1}",
                "days.one", "{0} a",
                "days.other", "{0} d");

            Assert.That(Describe(TableAudit.Compare(English, russian)),
                Is.EqualTo(new[] { "MissingPluralForm:days.few", "MissingPluralForm:days.many" }));
        }

        [Test]
        public void LanguageWithoutPlural_NeedsOnlyTheOtherForm()
        {
            StringTable korean = Table("ko",
                "menu.play", "x",
                "greeting", "{0}",
                "score", "{0} {1}",
                "days.other", "{0} d");

            Assert.That(TableAudit.Compare(English, korean), Is.Empty);
        }

        [Test]
        public void PluralForm_MayDropTheCountSlot_ButMustNotAddOne()
        {
            StringTable arabic = Table("ar",
                "menu.play", "x",
                "greeting", "{0}",
                "score", "{0} {1}",
                "days.zero", "none",         // spelling the number out is fine
                "days.one", "exactly one",
                "days.two", "a pair",
                "days.few", "{0} b",
                "days.many", "{0} c {1}",    // {1} is not supplied by the call
                "days.other", "{0} d");

            List<TableProblem> problems = TableAudit.Compare(English, arabic);

            Assert.That(Describe(problems), Is.EqualTo(new[] { "ExtraPlaceholder:days.many" }));
        }

        [Test]
        public void KeyThatMerelyEndsInOther_IsAnOrdinaryKey()
        {
            // "tab.other" has no "tab.one" next to it, so it is not a plural form and no plural forms
            // are demanded from the translation.
            StringTable source = Table("en", "tab.main", "Main", "tab.other", "Other");
            StringTable russian = Table("ru", "tab.main", "a", "tab.other", "b");

            Assert.That(TableAudit.Compare(source, russian), Is.Empty);
        }

        [Test]
        public void CheckPhrases_ReportsGapsAgainstTheOtherLocales_AndSlotDrift()
        {
            var german = new Dictionary<string, string>
            {
                { "Settings", "Einstellungen" },
                { "Round {0}", "Runde" },
                { "Quit", "" }
            };

            List<TableProblem> problems = TableAudit.CheckPhrases(german, new[] { "Settings", "Shop", "Quit", "Round {0}" });

            Assert.That(Describe(problems),
                Is.EqualTo(new[] { "MissingKey:Shop", "EmptyValue:Quit", "DroppedPlaceholder:Round {0}" }));
        }
    }

    public class PoolAuditTests
    {
        private static readonly string[] Source = { "Welcome, {0}!", "See you soon.", "{0} joined {1}." };

        private static PoolAuditResult Check(string[] localized, bool tableHasFile = true, AuditIgnoreList ignore = null)
        {
            var result = new PoolAuditResult("de");
            PoolAudit.Check("Dialogue", "greetings", Source, localized, tableHasFile, ignore, result);
            return result;
        }

        [Test]
        public void CompleteTranslation_HasNoProblems()
        {
            PoolAuditResult result = Check(new[] { "Willkommen, {0}!", "Bis bald.", "{0} ist {1} beigetreten." });

            Assert.That(result.Problems, Is.Empty);
            Assert.That(result.PoolCount, Is.EqualTo(1));
        }

        [Test]
        public void MissingPool_IsReported()
        {
            PoolAuditResult result = Check(null);

            Assert.That(result.Count(PoolProblemKind.MissingPool), Is.EqualTo(1));
            Assert.That(result.ProblemCount, Is.EqualTo(1));
        }

        [Test]
        public void TableWithoutAFile_IsOneNote_NotOneProblemPerPool()
        {
            var result = new PoolAuditResult("de");
            PoolAudit.Check("Dialogue", "greetings", Source, null, false, null, result);
            PoolAudit.Check("Dialogue", "farewells", Source, null, false, null, result);

            Assert.That(result.Problems, Is.Empty);
            Assert.That(result.TablesWithoutFile["Dialogue"], Is.EqualTo(2));
            Assert.That(result.PoolCount, Is.EqualTo(2));
        }

        [Test]
        public void LengthMismatch_IsReported()
        {
            PoolAuditResult result = Check(new[] { "Willkommen, {0}!", "Bis bald." });

            Assert.That(result.Count(PoolProblemKind.LengthMismatch), Is.EqualTo(1));
            Assert.That(result.Problems[0].Detail, Is.EqualTo("2 entries, the source has 3"));
        }

        [Test]
        public void BlankEntry_IsReportedWithItsIndex()
        {
            PoolAuditResult result = Check(new[] { "Willkommen, {0}!", "", "{0} ist {1} beigetreten." });

            Assert.That(result.Count(PoolProblemKind.EmptyElement), Is.EqualTo(1));
            Assert.That(result.Problems[0].Index, Is.EqualTo(1));
            Assert.That(result.Problems[0].ToString(), Is.EqualTo("EmptyElement Dialogue|greetings[1]"));
        }

        [Test]
        public void SlotDrift_IsReported()
        {
            PoolAuditResult result = Check(new[] { "Willkommen, {0} und {2}!", "Bis bald.", "{1} hat einen neuen Gast." });

            Assert.That(result.Count(PoolProblemKind.ExtraPlaceholder), Is.EqualTo(1));
            Assert.That(result.Count(PoolProblemKind.DroppedPlaceholder), Is.EqualTo(1));
            Assert.That(result.Problems[0].Detail, Is.EqualTo("{2}"));
            Assert.That(result.Problems[1].Index, Is.EqualTo(2));
            Assert.That(result.Problems[1].Detail, Is.EqualTo("{0}"));
        }

        [Test]
        public void PoolIdenticalToTheSource_IsListedButNotCountedAsAProblem()
        {
            PoolAuditResult result = Check((string[])Source.Clone());

            Assert.That(result.Count(PoolProblemKind.Untranslated), Is.EqualTo(1));
            Assert.That(result.ProblemCount, Is.EqualTo(0));
        }

        [Test]
        public void IgnoreList_CanCoverAWholePool()
        {
            var ignore = new AuditIgnoreList(new[] { "Dialogue|greetings   # proper names, kept as they are" });

            PoolAuditResult missing = Check(null, true, ignore);
            PoolAuditResult identical = Check((string[])Source.Clone(), true, ignore);

            Assert.That(missing.Problems, Is.Empty);
            Assert.That(missing.IgnoredCount, Is.EqualTo(1));
            Assert.That(identical.Problems, Is.Empty);
        }

        [Test]
        public void IgnoreList_CanCoverOneEntry()
        {
            var ignore = new AuditIgnoreList(new[] { "Dialogue|greetings[2]" });

            PoolAuditResult result = Check(new[] { "Willkommen, {0}!", "", "{1} hat einen neuen Gast." }, true, ignore);

            // Entry 2 drops a slot but was reviewed; entry 1 is still a problem.
            Assert.That(result.Count(PoolProblemKind.DroppedPlaceholder), Is.EqualTo(0));
            Assert.That(result.Count(PoolProblemKind.EmptyElement), Is.EqualTo(1));
            Assert.That(result.IgnoredCount, Is.EqualTo(1));
        }

        [Test]
        public void IgnoreList_SkipsCommentsAndBlankLines()
        {
            var ignore = new AuditIgnoreList(new[]
            {
                "# reviewed exceptions",
                "",
                "  Names|first  ",
                "Names|last[4] # one entry",
                "not an id"
            });

            Assert.That(ignore.Count, Is.EqualTo(2));
            Assert.That(ignore.ContainsPool("Names", "first"), Is.True);
            Assert.That(ignore.ContainsEntry("Names", "last", 4), Is.True);
            Assert.That(ignore.ContainsPool("Names", "last"), Is.False);
        }
    }

    public class LocaleCatalogTests
    {
        [Test]
        public void TargetLocales_ExcludeTheSourceLanguage_AndAreInOrdinalOrder()
        {
            Assert.That(Fixtures.Catalog().TargetLocales, Is.EqualTo(new[] { "ar", "de", "fr", "ko", "ru" }));
        }

        [Test]
        public void All_KeepsTheOrderGiven()
        {
            LocaleCatalog catalog = Fixtures.Catalog();

            Assert.That(catalog.All.Count, Is.EqualTo(6));
            Assert.That(catalog.All[0].Code, Is.EqualTo("en"));
            Assert.That(catalog.All[1].Code, Is.EqualTo("de"));
        }

        [Test]
        public void IsRtl_ComesFromTheCatalog()
        {
            LocaleCatalog catalog = Fixtures.Catalog();

            Assert.That(catalog.IsRtl("ar"), Is.True);
            Assert.That(catalog.IsRtl("de"), Is.False);
            Assert.That(catalog.IsRtl("xx"), Is.False);
        }

        [Test]
        public void FindBySystemLanguage_MatchesTheNamesListedForALocale()
        {
            var catalog = new LocaleCatalog("en", new[]
            {
                new LocaleInfo("en", "English", "English"),
                new LocaleInfo("zh-Hans", "\u7B80\u4F53\u4E2D\u6587", "Chinese (Simplified)", false,
                    new[] { "ChineseSimplified", "Chinese" })
            });

            Assert.That(catalog.FindBySystemLanguage("ChineseSimplified"), Is.EqualTo("zh-Hans"));
            Assert.That(catalog.FindBySystemLanguage("Chinese"), Is.EqualTo("zh-Hans"));
            Assert.That(catalog.FindBySystemLanguage("Klingon"), Is.Null);
            Assert.That(catalog.FindBySystemLanguage(null), Is.Null);
        }

        [Test]
        public void SourceLocale_MustBeListed()
        {
            Assert.Throws<System.ArgumentException>(
                () => new LocaleCatalog("en", new[] { new LocaleInfo("de", "Deutsch", "German") }));
        }

        [Test]
        public void DuplicateCodes_AreRejected()
        {
            Assert.Throws<System.ArgumentException>(() => new LocaleCatalog("en", new[]
            {
                new LocaleInfo("en", "English", "English"),
                new LocaleInfo("en", "English", "English")
            }));
        }
    }
}
