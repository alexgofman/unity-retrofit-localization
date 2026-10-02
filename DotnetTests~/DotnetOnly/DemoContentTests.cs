using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    /// <summary>
    /// Runs the package's own checks over the demo content shipped in Samples~, so the sample
    /// tables are held to the standard the tools set for anyone else's tables. Repository checks
    /// like these read files next to the sources, which is why they are not part of the tests that
    /// also run inside Unity.
    /// </summary>
    public class DemoContentTests
    {
        private const string DemoFolder = "Samples~/LanguagePickerDemo";

        private static readonly string[] Locales = { "de", "ru", "ko", "ar" };

        private sealed class DemoFiles : ITextSource
        {
            public string Load(string path)
            {
                string file = Path.Combine(RepositoryRoot(), DemoFolder, "Resources", path + ".json");
                return File.Exists(file) ? File.ReadAllText(file) : null;
            }
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "package.json"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Samples~")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("The package root was not found above the test directory.");
        }

        private static LocaleCatalog Catalog()
        {
            return new LocaleCatalog("en", new[]
            {
                new LocaleInfo("en", "English", "English"),
                new LocaleInfo("de", "Deutsch", "German"),
                new LocaleInfo("ru", "Russian", "Russian"),
                new LocaleInfo("ko", "Korean", "Korean"),
                new LocaleInfo("ar", "Arabic", "Arabic", true)
            });
        }

        private static LocalizerOptions Options()
        {
            return new LocalizerOptions
            {
                TableFolder = "RetrofitLocalizationDemo/Tables",
                PoolFolder = "RetrofitLocalizationDemo/Pools"
            };
        }

        private static Localizer NewLocalizer()
        {
            return new Localizer(Catalog(), new DemoFiles(), Options());
        }

        private static string ReadSample(string fileName)
        {
            return File.ReadAllText(Path.Combine(RepositoryRoot(), DemoFolder, fileName));
        }

        // The source-language pools are literals in DemoTips.cs: L("key", new[] { "...", "..." })
        private static Dictionary<string, string[]> SourcePools()
        {
            var pools = new Dictionary<string, string[]>();
            string code = ReadSample("DemoTips.cs");
            foreach (Match pool in Regex.Matches(code, "L\\(\"(\\w+)\",\\s*new\\[\\]\\s*\\{(.*?)\\}\\)", RegexOptions.Singleline))
            {
                var entries = new List<string>();
                foreach (Match literal in Regex.Matches(pool.Groups[2].Value, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                {
                    entries.Add(Regex.Unescape(literal.Groups[1].Value));
                }

                pools[pool.Groups[1].Value] = entries.ToArray();
            }

            return pools;
        }

        [Test]
        public void DemoTables_PassTheTableValidation()
        {
            AuditOutcome outcome = LocalizationAudit.ValidateTables(Catalog(), Options(), new DemoFiles());

            Assert.That(outcome.ProblemCount, Is.EqualTo(0), outcome.Summary + outcome.Details);
        }

        [Test]
        public void DemoPools_PassThePoolAudit_InEveryLocale()
        {
            Dictionary<string, string[]> sourcePools = SourcePools();
            Assert.That(sourcePools.Count, Is.EqualTo(2), "expected the two pools of DemoTips.cs");

            Localizer localizer = NewLocalizer();
            foreach (string locale in Locales)
            {
                PoolTable table = localizer.LoadPoolTable("DemoTips", locale);
                Assert.That(table, Is.Not.Null, "DemoTips_" + locale + ".json");

                var result = new PoolAuditResult(locale);
                foreach (KeyValuePair<string, string[]> pool in sourcePools)
                {
                    table.TryGet(pool.Key, out string[] localized);
                    PoolAudit.Check("DemoTips", pool.Key, pool.Value, localized, true, null, result);
                }

                Assert.That(result.Problems, Is.Empty, locale + ": " + string.Join("; ", result.Problems));
            }
        }

        [Test]
        public void EveryKeyTheDemoScriptUses_IsInTheSourceTable()
        {
            StringTable english = StringTable.Parse("en", new DemoFiles().Load(Options().TablePath("en")));
            string code = ReadSample("LanguagePickerDemo.cs");

            int checkedKeys = 0;
            foreach (Match call in Regex.Matches(code, "(GetPlural|GetRaw|Get|AddKeyed|\\.Key =)[ (]\"([a-z_.]+)\""))
            {
                string key = call.Groups[2].Value;
                if (key == "demo.not_in_any_table")
                {
                    Assert.That(english.Contains(key), Is.False, "this key is meant to be missing");
                    continue;
                }

                string expected = call.Groups[1].Value == "GetPlural" ? key + ".other" : key;
                Assert.That(english.Contains(expected), Is.True, expected);
                checkedKeys++;
            }

            Assert.That(checkedKeys, Is.GreaterThan(15));
        }

        [Test]
        public void EveryLiteralLabelOfTheDemoScript_IsInEveryPhraseTable()
        {
            string code = ReadSample("LanguagePickerDemo.cs");
            var phrases = new List<string>();
            foreach (Match label in Regex.Matches(code, "AddLabel\\(\"([^\"]+)\"\\)")) phrases.Add(label.Groups[1].Value);
            Assert.That(phrases.Count, Is.GreaterThan(3));

            foreach (string locale in Locales)
            {
                Localizer localizer = NewLocalizer();
                localizer.RtlShaper = new BracketShaper();
                localizer.SetLocale(locale);
                foreach (string phrase in phrases)
                {
                    Assert.That(localizer.TryTranslate(phrase, out _), Is.True, locale + ": " + phrase);
                }
            }
        }

        [Test]
        public void EveryDemoKey_ResolvesWithoutAFallback_InEveryLocale()
        {
            StringTable english = StringTable.Parse("en", new DemoFiles().Load(Options().TablePath("en")));

            foreach (string locale in Locales)
            {
                Localizer localizer = NewLocalizer();
                localizer.SetLocale(locale);
                foreach (string key in english.Keys)
                {
                    if (key.EndsWith(".one", StringComparison.Ordinal)) continue;
                    if (key.EndsWith(".other", StringComparison.Ordinal))
                    {
                        string pluralKey = key.Substring(0, key.Length - ".other".Length);
                        foreach (int count in new[] { 0, 1, 2, 3, 5, 11, 21, 102 }) localizer.GetPlural(pluralKey, count);
                    }
                    else
                    {
                        localizer.Get(key, "x");
                    }
                }

                Assert.That(localizer.Fallbacks, Is.Empty, locale + ": " + string.Join("; ", localizer.Fallbacks));
            }
        }

        [Test]
        public void KoreanDemoLines_PickTheParticleForTheInsertedWord()
        {
            Localizer localizer = NewLocalizer();
            localizer.SetLocale("ko");

            string item = localizer.GetRaw("sample.item");   // ends in a vowel
            string place = localizer.GetRaw("sample.place"); // ends in rieul

            // Whatever the verb, the particle right after the inserted word is what matters here.
            Assert.That(localizer.Get("inbox.opened", item), Does.StartWith(item + "\uB97C "));   // object particle after a vowel
            Assert.That(localizer.Get("folder.filed", place), Does.StartWith(place + "\uB85C ")); // direction particle after rieul
        }

        [Test]
        public void RussianDemoPlurals_FollowTheCount()
        {
            Localizer localizer = NewLocalizer();
            localizer.SetLocale("ru");

            Assert.That(localizer.GetPlural("files.count", 1), Does.EndWith("\u0444\u0430\u0439\u043B"));
            Assert.That(localizer.GetPlural("files.count", 3), Does.EndWith("\u0444\u0430\u0439\u043B\u0430"));
            Assert.That(localizer.GetPlural("files.count", 5), Does.EndWith("\u0444\u0430\u0439\u043B\u043E\u0432"));
            Assert.That(localizer.GetPlural("files.count", 21), Does.EndWith("\u0444\u0430\u0439\u043B"));
        }

        [Test]
        public void ArabicDemoSentences_ShapeBackToWhatWasTyped()
        {
            // For a sentence without numbers, Latin words or tags, shaping amounts to "join the
            // letters, then reverse the line". Undoing the reversal and normalising the presentation
            // forms back to plain letters therefore has to give the sentence that was typed. Vowel
            // marks are left out of the comparison: they stay behind their letter in both orders.
            StringTable arabic = StringTable.Parse("ar", new DemoFiles().Load(Options().TablePath("ar")));

            int sentences = 0;
            foreach (string key in arabic.Keys)
            {
                arabic.TryGet(key, out string typed);
                if (!IsPlainArabic(typed)) continue;

                string shaped = BasicArabicShaper.Instance.Shape(typed);
                Assert.That(RtlText.ContainsArabicLetters(shaped), Is.False, key);

                char[] visual = shaped.ToCharArray();
                Array.Reverse(visual);
                string restored = new string(visual).Normalize(NormalizationForm.FormKC);

                Assert.That(WithoutMarks(restored), Is.EqualTo(WithoutMarks(typed.Normalize(NormalizationForm.FormKC))), key);
                sentences++;
            }

            Assert.That(sentences, Is.GreaterThan(8));
        }

        private static bool IsPlainArabic(string text)
        {
            if (!RtlText.ContainsArabicLetters(text)) return false;
            foreach (char c in text)
            {
                if (c < 128 && (char.IsLetterOrDigit(c) || c == '{' || c == '<' || c == '(' || c == '[')) return false;
            }

            return true;
        }

        private static string WithoutMarks(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c < '\u064B' || c > '\u065F') builder.Append(c);
            }

            return builder.ToString();
        }

        [Test]
        public void ArabicDemoPrice_KeepsItsColourTagAroundTheNumber()
        {
            Localizer localizer = NewLocalizer();
            localizer.SetLocale("ar");

            string shaped = localizer.Get("price.label", 250);

            Assert.That(shaped, Does.Contain("<color=#FFD54A>250</color>"));
            Assert.That(RtlText.ContainsArabicLetters(shaped), Is.False, "every Arabic letter should be shaped");
        }
    }
}
