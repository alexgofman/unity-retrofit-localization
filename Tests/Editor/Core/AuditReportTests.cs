using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    public class LocalizationAuditTests
    {
        [Test]
        public void ValidateTables_ListsTheGapsOfEveryLocale()
        {
            AuditOutcome outcome = LocalizationAudit.ValidateTables(Fixtures.Catalog(), null, Fixtures.Files());

            Assert.That(outcome.ProblemCount, Is.GreaterThan(0));
            // German lacks two keys and adds a slot the source text does not have.
            Assert.That(outcome.Details, Does.Contain("MissingKey menu.quit"));
            Assert.That(outcome.Details, Does.Contain("MissingKey item.picked"));
            Assert.That(outcome.Details, Does.Contain("ExtraPlaceholder filed.under ({3})"));
            // French has no table file at all.
            Assert.That(outcome.Details, Does.Contain("Table Localization/fr: file not found"));
            Assert.That(outcome.Summary, Does.Contain("  fr: 1 problem(s)"));
        }

        [Test]
        public void ValidateTables_ChecksPhraseTablesAcrossLocales()
        {
            AuditOutcome outcome = LocalizationAudit.ValidateTables(Fixtures.Catalog(), null, Fixtures.Files());

            // "New Game" is translated in Russian only; Korean has no phrase table while others do.
            Assert.That(outcome.Details, Does.Contain("=== de phrases"));
            Assert.That(outcome.Details, Does.Contain("MissingKey New Game"));
            Assert.That(outcome.Details, Does.Contain("Table Localization/phrases_ko: file not found"));
        }

        [Test]
        public void ValidateTables_CleanTables_ReportNoProblems()
        {
            var catalog = new LocaleCatalog("en", new[]
            {
                new LocaleInfo("en", "English", "English"),
                new LocaleInfo("de", "Deutsch", "German")
            });
            MemoryTextSource files = new MemoryTextSource()
                .With("Localization/en", "{ \"a\": \"One {0}\", \"b.one\": \"{0} x\", \"b.other\": \"{0} xs\" }")
                .With("Localization/de", "{ \"a\": \"Eins {0}\", \"b.one\": \"{0} y\", \"b.other\": \"{0} ys\" }");

            AuditOutcome outcome = LocalizationAudit.ValidateTables(catalog, null, files);

            Assert.That(outcome.ProblemCount, Is.EqualTo(0));
            Assert.That(outcome.Summary, Does.Contain("  de: OK"));
            Assert.That(outcome.Details, Is.Empty);
        }

        [Test]
        public void ValidateTables_WithoutASourceTable_SaysSo()
        {
            AuditOutcome outcome = LocalizationAudit.ValidateTables(Fixtures.Catalog(), null, new MemoryTextSource());

            Assert.That(outcome.ProblemCount, Is.EqualTo(1));
            Assert.That(outcome.Summary, Does.Contain("Localization/en: file not found"));
        }

        [Test]
        public void ValidateTables_ReportsDuplicateKeys()
        {
            var catalog = new LocaleCatalog("en", new[]
            {
                new LocaleInfo("en", "English", "English"),
                new LocaleInfo("de", "Deutsch", "German")
            });
            MemoryTextSource files = new MemoryTextSource()
                .With("Localization/en", "{ \"a\": \"One\" }")
                .With("Localization/de", "{ \"a\": \"Eins\", \"a\": \"Zwei\" }");

            AuditOutcome outcome = LocalizationAudit.ValidateTables(catalog, null, files);

            Assert.That(outcome.ProblemCount, Is.EqualTo(1));
            Assert.That(outcome.Details, Does.Contain("DuplicateKey a"));
        }

        [Test]
        public void AuditPools_FindsWhatTheRuntimeWouldHideBehindAFallback()
        {
            Localizer localizer = Fixtures.Localizer();
            string[] loading = localizer.LocalizePool("Tips", "loading", new[] { "Tip one", "Tip two", "Tip three" });
            string[] tooShort = localizer.LocalizePool("Tips", "short", new[] { "First", "Second" });
            string[] gaps = localizer.LocalizePool("Tips", "gaps", new[] { "First", "Second", "Third" });
            string[] dialogue = localizer.LocalizePool("Dialogue", "greeting", new[] { "Hello" });

            AuditOutcome outcome = LocalizationAudit.AuditPools(localizer);

            // German: one pool of the wrong length, one blank entry. Russian: two pools missing from
            // an existing table. Tables without any file are noted, not counted.
            Assert.That(outcome.ProblemCount, Is.EqualTo(4));
            Assert.That(outcome.Details, Does.Contain("LengthMismatch Tips|short (1 entries, the source has 2)"));
            Assert.That(outcome.Details, Does.Contain("EmptyElement Tips|gaps[1]"));
            Assert.That(outcome.Details, Does.Contain("MissingPool Tips|short"));
            Assert.That(outcome.Details, Does.Contain("MissingPool Tips|gaps"));
            Assert.That(outcome.Details, Does.Contain("NoFile Dialogue (1 pools stay in the source language)"));
            Assert.That(outcome.Summary, Does.Contain("String pools: 4 registered, 5 locale(s)."));
            Assert.That(outcome.Summary, Does.Contain("  de: 2 problem(s)"));

            GC.KeepAlive(loading);
            GC.KeepAlive(tooShort);
            GC.KeepAlive(gaps);
            GC.KeepAlive(dialogue);
        }

        [Test]
        public void AuditPools_NeitherSwitchesTheLocaleNorTouchesThePools()
        {
            Localizer localizer = Fixtures.Localizer();
            string[] loading = localizer.LocalizePool("Tips", "loading", new[] { "Tip one", "Tip two", "Tip three" });
            int languageChanges = 0;
            localizer.LanguageChanged += () => languageChanges++;

            LocalizationAudit.AuditPools(localizer);

            Assert.That(localizer.CurrentLocale, Is.EqualTo("en"));
            Assert.That(languageChanges, Is.EqualTo(0));
            Assert.That(loading, Is.EqualTo(new[] { "Tip one", "Tip two", "Tip three" }));
        }

        [Test]
        public void AuditPools_HonoursTheIgnoreList()
        {
            Localizer localizer = Fixtures.Localizer();
            string[] tooShort = localizer.LocalizePool("Tips", "short", new[] { "First", "Second" });
            var ignore = new AuditIgnoreList(new[] { "Tips|short" });

            AuditOutcome outcome = LocalizationAudit.AuditPools(localizer, ignore);

            // The Russian table simply lacks the pool, which the list covers. The German pool has the
            // wrong length, and a length mismatch is never something to wave through.
            Assert.That(outcome.ProblemCount, Is.EqualTo(1));
            Assert.That(outcome.Details, Does.Contain("LengthMismatch Tips|short"));
            Assert.That(outcome.Details, Does.Not.Contain("MissingPool"));

            GC.KeepAlive(tooShort);
        }
    }

    public class PoolOwnerTypesTests
    {
        private const string Namespace = "RetrofitLocalization.Tests.PoolOwners";

        [Test]
        public void DeclaresStringPools_LooksForStaticArraysAndLists()
        {
            Assert.That(PoolOwnerTypes.DeclaresStringPools(typeof(PoolOwners.WithArray)), Is.True);
            Assert.That(PoolOwnerTypes.DeclaresStringPools(typeof(PoolOwners.WithList)), Is.True);
            Assert.That(PoolOwnerTypes.DeclaresStringPools(typeof(PoolOwners.Outer.Nested)), Is.True);
            Assert.That(PoolOwnerTypes.DeclaresStringPools(typeof(PoolOwners.WithoutPools)), Is.False);
            Assert.That(PoolOwnerTypes.DeclaresStringPools(typeof(PoolOwners.Outer)), Is.False);
            Assert.That(PoolOwnerTypes.DeclaresStringPools(null), Is.False);
        }

        [Test]
        public void Initialize_RunsTheStaticConstructorOfEveryPoolOwnerInTheNamespace()
        {
            var failures = new List<string>();

            int initialised = PoolOwnerTypes.Initialize(new[] { typeof(PoolOwnerTypesTests).Assembly },
                new[] { Namespace }, failures);

            Assert.That(initialised, Is.EqualTo(3));
            Assert.That(PoolOwners.InitLog.Seen, Does.Contain("WithArray"));
            Assert.That(PoolOwners.InitLog.Seen, Does.Contain("WithList"));
            Assert.That(PoolOwners.InitLog.Seen, Does.Contain("Nested"));
            Assert.That(PoolOwners.InitLog.Seen, Does.Not.Contain("WithoutPools"));
        }

        [Test]
        public void Initialize_ReportsTypesThatCannotInitialise_AndCarriesOn()
        {
            var failures = new List<string>();

            PoolOwnerTypes.Initialize(new[] { typeof(PoolOwnerTypesTests).Assembly }, new[] { Namespace }, failures);

            Assert.That(failures.Count, Is.EqualTo(1));
            Assert.That(failures[0], Does.Contain("Failing"));
            Assert.That(failures[0], Does.Contain("InvalidOperationException"));
            Assert.That(failures[0], Does.Contain("needs a loaded save"));
        }

        [Test]
        public void Initialize_SkipsOtherNamespaces()
        {
            int initialised = PoolOwnerTypes.Initialize(new[] { typeof(PoolOwnerTypesTests).Assembly },
                new[] { "RetrofitLocalization.Tests.Elsewhere" }, new List<string>());

            Assert.That(initialised, Is.EqualTo(0));
        }
    }
}

namespace RetrofitLocalization.Tests.PoolOwners
{
    // Stand-ins for the classes of a code base that keep their prose in static fields.

    internal static class InitLog
    {
        public static readonly HashSet<string> Seen = new HashSet<string>();

        public static string[] Note(string owner, params string[] pool)
        {
            Seen.Add(owner);
            return pool;
        }
    }

    internal static class WithArray
    {
        private static readonly string[] Lines = InitLog.Note("WithArray", "one", "two");

        public static int Count => Lines.Length;
    }

    internal static class WithList
    {
        internal static readonly List<string> Lines = new List<string>(InitLog.Note("WithList", "one"));
    }

    internal static class WithoutPools
    {
        internal static readonly int[] Numbers = MakeNumbers();

        private static int[] MakeNumbers()
        {
            InitLog.Seen.Add("WithoutPools");
            return new[] { 1, 2, 3 };
        }
    }

    internal static class Failing
    {
        internal static readonly string[] Lines = Throw();

        private static string[] Throw()
        {
            throw new InvalidOperationException("needs a loaded save");
        }
    }

    internal class Outer
    {
        internal static class Nested
        {
            internal static readonly string[] Lines = InitLog.Note("Nested", "inner");
        }
    }
}
