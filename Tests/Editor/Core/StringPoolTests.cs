using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    public class StringPoolTests
    {
        private static readonly string[] EnglishTips = { "Tip one", "Tip two", "Tip three" };
        private static readonly string[] GermanTips = { "Tipp eins", "Tipp zwei", "Tipp drei" };
        private static readonly string[] RussianTips =
        {
            "\u0421\u043E\u0432\u0435\u0442 \u043E\u0434\u0438\u043D",
            "\u0421\u043E\u0432\u0435\u0442 \u0434\u0432\u0430",
            "\u0421\u043E\u0432\u0435\u0442 \u0442\u0440\u0438"
        };

        private Localizer _localizer;
        private List<FallbackReport> _reports;

        [SetUp]
        public void SetUp()
        {
            _localizer = Fixtures.Localizer();
            _reports = new List<FallbackReport>();
            _localizer.FallbackReported += _reports.Add;
        }

        private static string[] NewEnglishTips()
        {
            return (string[])EnglishTips.Clone();
        }

        private bool WasReported(FallbackKind kind, string table, string key)
        {
            foreach (FallbackReport report in _reports)
            {
                if (report.Kind == kind && report.Table == table && report.Key == key) return true;
            }

            return false;
        }

        [Test]
        public void SourceLanguage_ReturnsTheVeryArrayItWasGiven_Untouched()
        {
            string[] pool = NewEnglishTips();

            string[] result = _localizer.LocalizePool("Tips", "loading", pool);

            Assert.That(result, Is.SameAs(pool));
            Assert.That(pool, Is.EqualTo(EnglishTips));
            Assert.That(_reports, Is.Empty);
        }

        [Test]
        public void LanguageChange_RewritesThePoolInPlace()
        {
            string[] pool = _localizer.LocalizePool("Tips", "loading", NewEnglishTips());
            string[] cachedByACallSite = pool;

            _localizer.SetLocale("de");

            Assert.That(cachedByACallSite, Is.SameAs(pool));
            Assert.That(cachedByACallSite, Is.EqualTo(GermanTips));
        }

        [Test]
        public void RoundTrip_SourceToTranslationAndBack_RestoresTheSourceTextExactly()
        {
            string[] pool = _localizer.LocalizePool("Tips", "loading", NewEnglishTips());

            _localizer.SetLocale("de");
            Assert.That(pool, Is.EqualTo(GermanTips));

            _localizer.SetLocale("en");
            Assert.That(pool, Is.EqualTo(EnglishTips));

            // And again, to show the stored source text was not overwritten by the first trip.
            _localizer.SetLocale("ru");
            Assert.That(pool, Is.EqualTo(RussianTips));
            _localizer.SetLocale("en");
            Assert.That(pool, Is.EqualTo(EnglishTips));
        }

        [Test]
        public void SwitchingBetweenTwoTranslations_DoesNotNeedTheSourceLanguageInBetween()
        {
            string[] pool = _localizer.LocalizePool("Tips", "loading", NewEnglishTips());

            _localizer.SetLocale("de");
            _localizer.SetLocale("ru");

            Assert.That(pool, Is.EqualTo(RussianTips));
        }

        [Test]
        public void PoolCreatedWhileATranslationIsActive_IsTranslatedAtOnce_AndStillRestorable()
        {
            _localizer.SetLocale("de");

            string[] pool = NewEnglishTips();
            string[] result = _localizer.LocalizePool("Tips", "loading", pool);

            Assert.That(result, Is.SameAs(pool));
            Assert.That(pool, Is.EqualTo(GermanTips));

            _localizer.SetLocale("en");
            Assert.That(pool, Is.EqualTo(EnglishTips));
        }

        [Test]
        public void LengthMismatch_IsRefused_ThePoolStaysInTheSourceLanguage_AndItIsReported()
        {
            // The German table has one entry for "short"; the source has two.
            string[] pool = _localizer.LocalizePool("Tips", "short", new[] { "First", "Second" });

            _localizer.SetLocale("de");

            Assert.That(pool, Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(WasReported(FallbackKind.PoolLengthMismatch, "Tips", "short"), Is.True);
        }

        [Test]
        public void MissingPoolKey_StaysInTheSourceLanguage_AndIsReported()
        {
            string[] pool = _localizer.LocalizePool("Tips", "not.in.any.table", new[] { "Only English" });

            _localizer.SetLocale("de");

            Assert.That(pool, Is.EqualTo(new[] { "Only English" }));
            Assert.That(WasReported(FallbackKind.MissingPool, "Tips", "not.in.any.table"), Is.True);
        }

        [Test]
        public void MissingPoolTableFile_IsReportedOncePerTable_NotOncePerPool()
        {
            MemoryTextSource files = Fixtures.Files();
            var localizer = new Localizer(Fixtures.Catalog(), files);
            localizer.FallbackReported += _reports.Add;
            string[] first = localizer.LocalizePool("Dialogue", "greeting", new[] { "Hello" });
            string[] second = localizer.LocalizePool("Dialogue", "farewell", new[] { "Bye" });

            localizer.SetLocale("de");

            Assert.That(first, Is.EqualTo(new[] { "Hello" }));
            Assert.That(second, Is.EqualTo(new[] { "Bye" }));
            Assert.That(_reports.Count, Is.EqualTo(1));
            Assert.That(_reports[0].Kind, Is.EqualTo(FallbackKind.MissingPoolTable));
            Assert.That(_reports[0].Table, Is.EqualTo("Dialogue"));
            Assert.That(files.LoadCount("Localization/Pools/Dialogue_de"), Is.EqualTo(1));
        }

        [Test]
        public void BlankEntry_FallsBackOnItsOwn_AndIsReported()
        {
            string[] pool = _localizer.LocalizePool("Tips", "gaps", new[] { "First", "Second", "Third" });

            _localizer.SetLocale("de");

            Assert.That(pool, Is.EqualTo(new[] { "Erster", "Second", "Dritter" }));
            Assert.That(WasReported(FallbackKind.EmptyPoolElement, "Tips", "gaps[1]"), Is.True);
        }

        [Test]
        public void BlankEntry_IsStillVisibleToAnAudit()
        {
            string[] pool = _localizer.LocalizePool("Tips", "gaps", new[] { "First", "Second", "Third" });
            _localizer.SetLocale("de");
            Assert.That(pool[1], Is.EqualTo("Second"));

            // The table as written still has the blank: resolving a pool never edits the table.
            PoolTable raw = _localizer.LoadPoolTable("Tips", "de");
            Assert.That(raw.TryGet("gaps", out string[] written), Is.True);
            Assert.That(written[1], Is.Empty);
        }

        [Test]
        public void ListPool_IsRewrittenInPlaceToo()
        {
            var pool = new List<string>(EnglishTips);

            List<string> result = _localizer.LocalizePool("Tips", "loading", pool);
            _localizer.SetLocale("de");

            Assert.That(result, Is.SameAs(pool));
            Assert.That(pool, Is.EqualTo(GermanTips));

            _localizer.SetLocale("en");
            Assert.That(pool, Is.EqualTo(EnglishTips));
        }

        [Test]
        public void ListPool_ARunTimeChangeIsResetToTheAuthoredItems()
        {
            var pool = new List<string>(EnglishTips);
            _localizer.LocalizePool("Tips", "loading", pool);
            pool.RemoveAt(0);
            pool.Add("added at run time");

            _localizer.SetLocale("de");

            Assert.That(pool, Is.EqualTo(GermanTips));
        }

        [Test]
        public void RegisteringTheSameArrayAgain_KeepsTheOriginalSourceText()
        {
            string[] pool = _localizer.LocalizePool("Tips", "loading", NewEnglishTips());
            _localizer.SetLocale("de");

            // The array now holds German. Registering it again must not take that for source text.
            _localizer.LocalizePool("Tips", "loading", pool);
            _localizer.SetLocale("en");

            Assert.That(pool, Is.EqualTo(EnglishTips));
            Assert.That(_localizer.Pools.Count, Is.EqualTo(1));
            GC.KeepAlive(pool);
        }

        [Test]
        public void TwoArraysWithTheSameKey_AreBothKeptUpToDate()
        {
            string[] first = _localizer.LocalizePool("Tips", "loading", NewEnglishTips());
            string[] second = _localizer.LocalizePool("Tips", "loading", NewEnglishTips());

            _localizer.SetLocale("de");

            Assert.That(first, Is.EqualTo(GermanTips));
            Assert.That(second, Is.EqualTo(GermanTips));
            Assert.That(_localizer.Pools.Count, Is.EqualTo(2));
            GC.KeepAlive(first);
            GC.KeepAlive(second);
        }

        [Test]
        public void Pools_AreRewrittenBeforeLanguageChangedIsRaised()
        {
            string[] pool = _localizer.LocalizePool("Tips", "loading", NewEnglishTips());
            string seenBySubscriber = null;
            _localizer.LanguageChanged += () => seenBySubscriber = pool[0];

            _localizer.SetLocale("de");

            Assert.That(seenBySubscriber, Is.EqualTo("Tipp eins"));
        }

        [Test]
        public void SharedRegistry_SurvivesANewLocalizer()
        {
            // A registry can outlive the localizer that filled it. A new localizer that starts in
            // another locale brings the existing pools in line as soon as it loads.
            var registry = new StringPoolRegistry();
            var first = new Localizer(Fixtures.Catalog(), Fixtures.Files(), null, registry);
            string[] pool = first.LocalizePool("Tips", "loading", NewEnglishTips());
            first.SetLocale("de");

            var second = new Localizer(Fixtures.Catalog(), Fixtures.Files(), null, registry);
            second.Load("ru");

            Assert.That(pool, Is.EqualTo(RussianTips));
        }

        [Test]
        public void PoolTrackedWithoutBeingLocalized_IsBroughtInLineByRefreshPools()
        {
            // The situation of a pool that registers at a moment when the tables cannot be read:
            // it is only tracked, keeps its source text, and gets its translation afterwards.
            _localizer.SetLocale("de");
            string[] pool = NewEnglishTips();
            _localizer.Pools.Track("Tips", "loading", pool);
            Assert.That(pool, Is.EqualTo(EnglishTips));

            _localizer.RefreshPools();

            Assert.That(pool, Is.EqualTo(GermanTips));
        }

        [Test]
        public void Registration_FromSeveralThreads_LosesNothing()
        {
            // A static initializer can run on a loading thread, so registering has to be safe there.
            var registry = new StringPoolRegistry();
            var pools = new string[400][];
            for (int i = 0; i < pools.Length; i++) pools[i] = new[] { "entry " + i };

            System.Threading.Tasks.Parallel.For(0, pools.Length,
                i => registry.Track("Table" + (i % 7), "key" + (i % 13), pools[i]));

            Assert.That(registry.Count, Is.EqualTo(pools.Length));
            GC.KeepAlive(pools);
        }

        [Test]
        public void NullPool_IsPassedThrough()
        {
            Assert.That(_localizer.LocalizePool("Tips", "loading", (string[])null), Is.Null);
            Assert.That(_localizer.LocalizePool("Tips", "loading", (List<string>)null), Is.Null);
        }

        [Test]
        public void Registry_ReportsEveryLivePool()
        {
            string[] loading = _localizer.LocalizePool("Tips", "loading", NewEnglishTips());
            string[] shortPool = _localizer.LocalizePool("Tips", "short", new[] { "First", "Second" });
            var seen = new List<string>();

            _localizer.Pools.ForEach((table, key, source) => seen.Add(table + "|" + key + "|" + source.Length));

            seen.Sort(StringComparer.Ordinal);
            Assert.That(seen, Is.EqualTo(new[] { "Tips|loading|3", "Tips|short|2" }));

            // The registry only holds weak references, so keep the pools alive until here.
            GC.KeepAlive(loading);
            GC.KeepAlive(shortPool);
        }
    }
}
