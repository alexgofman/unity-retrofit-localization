using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    public class PluralRulesTests
    {
        [TestCase(0, PluralCategory.Other)]
        [TestCase(1, PluralCategory.One)]
        [TestCase(2, PluralCategory.Other)]
        [TestCase(11, PluralCategory.Other)]
        [TestCase(21, PluralCategory.Other)]
        [TestCase(-1, PluralCategory.One)]
        public void English_HasOneAndOther(long count, PluralCategory expected)
        {
            Assert.That(PluralRules.GetCategory("en", count), Is.EqualTo(expected));
        }

        [TestCase(0, PluralCategory.One)]
        [TestCase(1, PluralCategory.One)]
        [TestCase(2, PluralCategory.Other)]
        public void French_TreatsZeroAsSingular(long count, PluralCategory expected)
        {
            Assert.That(PluralRules.GetCategory("fr", count), Is.EqualTo(expected));
        }

        [TestCase(1, PluralCategory.One)]
        [TestCase(2, PluralCategory.Few)]
        [TestCase(3, PluralCategory.Few)]
        [TestCase(4, PluralCategory.Few)]
        [TestCase(5, PluralCategory.Many)]
        [TestCase(0, PluralCategory.Many)]
        [TestCase(10, PluralCategory.Many)]
        [TestCase(11, PluralCategory.Many)]
        [TestCase(12, PluralCategory.Many)]
        [TestCase(14, PluralCategory.Many)]
        [TestCase(20, PluralCategory.Many)]
        [TestCase(21, PluralCategory.One)]
        [TestCase(22, PluralCategory.Few)]
        [TestCase(25, PluralCategory.Many)]
        [TestCase(101, PluralCategory.One)]
        [TestCase(111, PluralCategory.Many)]
        [TestCase(112, PluralCategory.Many)]
        [TestCase(122, PluralCategory.Few)]
        public void Russian_UsesTheLastDigitExceptForTheTeens(long count, PluralCategory expected)
        {
            Assert.That(PluralRules.GetCategory("ru", count), Is.EqualTo(expected));
        }

        [TestCase(1, PluralCategory.One)]
        [TestCase(2, PluralCategory.Few)]
        [TestCase(4, PluralCategory.Few)]
        [TestCase(5, PluralCategory.Many)]
        [TestCase(0, PluralCategory.Many)]
        [TestCase(12, PluralCategory.Many)]
        [TestCase(14, PluralCategory.Many)]
        [TestCase(22, PluralCategory.Few)]
        [TestCase(112, PluralCategory.Many)]
        // Where Polish and Russian part ways: only the number 1 itself is singular.
        [TestCase(21, PluralCategory.Many)]
        [TestCase(101, PluralCategory.Many)]
        public void Polish_OnlyTheNumberOneIsSingular(long count, PluralCategory expected)
        {
            Assert.That(PluralRules.GetCategory("pl", count), Is.EqualTo(expected));
        }

        [TestCase(0, PluralCategory.Zero)]
        [TestCase(1, PluralCategory.One)]
        [TestCase(2, PluralCategory.Two)]
        [TestCase(3, PluralCategory.Few)]
        [TestCase(10, PluralCategory.Few)]
        [TestCase(11, PluralCategory.Many)]
        [TestCase(99, PluralCategory.Many)]
        [TestCase(100, PluralCategory.Other)]
        [TestCase(101, PluralCategory.Other)]
        [TestCase(102, PluralCategory.Other)]
        [TestCase(103, PluralCategory.Few)]
        [TestCase(111, PluralCategory.Many)]
        [TestCase(1000, PluralCategory.Other)]
        public void Arabic_UsesAllSixCategories(long count, PluralCategory expected)
        {
            Assert.That(PluralRules.GetCategory("ar", count), Is.EqualTo(expected));
        }

        [TestCase(1, PluralCategory.One)]
        [TestCase(2, PluralCategory.Few)]
        [TestCase(4, PluralCategory.Few)]
        [TestCase(5, PluralCategory.Other)]
        [TestCase(22, PluralCategory.Other)]
        public void Czech_CountsTwoToFourAsFew(long count, PluralCategory expected)
        {
            Assert.That(PluralRules.GetCategory("cs", count), Is.EqualTo(expected));
        }

        [TestCase("ko")]
        [TestCase("ja")]
        [TestCase("zh-Hans")]
        [TestCase("th")]
        public void LanguagesWithoutGrammaticalPlural_AlwaysUseOther(string locale)
        {
            Assert.That(PluralRules.GetCategory(locale, 0), Is.EqualTo(PluralCategory.Other));
            Assert.That(PluralRules.GetCategory(locale, 1), Is.EqualTo(PluralCategory.Other));
            Assert.That(PluralRules.GetCategory(locale, 5), Is.EqualTo(PluralCategory.Other));
        }

        [Test]
        public void RegionSubtag_FallsBackToTheLanguage()
        {
            Assert.That(PluralRules.GetCategory("en-GB", 1), Is.EqualTo(PluralCategory.One));
            Assert.That(PluralRules.GetCategory("ru_RU", 3), Is.EqualTo(PluralCategory.Few));
        }

        [Test]
        public void Portuguese_DependsOnTheRegion()
        {
            Assert.That(PluralRules.GetCategory("pt", 0), Is.EqualTo(PluralCategory.One));
            Assert.That(PluralRules.GetCategory("pt-BR", 0), Is.EqualTo(PluralCategory.One));
            Assert.That(PluralRules.GetCategory("pt-PT", 0), Is.EqualTo(PluralCategory.Other));
            Assert.That(PluralRules.GetCategory("pt-PT", 1), Is.EqualTo(PluralCategory.One));
        }

        [Test]
        public void UnknownOrMissingLocale_UsesOther()
        {
            Assert.That(PluralRules.GetCategory("xx", 1), Is.EqualTo(PluralCategory.Other));
            Assert.That(PluralRules.GetCategory(null, 1), Is.EqualTo(PluralCategory.Other));
            Assert.That(PluralRules.GetCategory("", 1), Is.EqualTo(PluralCategory.Other));
        }

        [Test]
        public void SmallestLong_DoesNotOverflow()
        {
            Assert.That(PluralRules.GetCategory("ru", long.MinValue), Is.EqualTo(PluralCategory.Many));
        }

        [Test]
        public void IntegerCategories_ListWhatEachLanguageNeeds()
        {
            Assert.That(PluralRules.IntegerCategories("en"),
                Is.EqualTo(new[] { PluralCategory.One, PluralCategory.Other }));
            Assert.That(PluralRules.IntegerCategories("ru"),
                Is.EqualTo(new[] { PluralCategory.One, PluralCategory.Few, PluralCategory.Many }));
            Assert.That(PluralRules.IntegerCategories("ko"), Is.EqualTo(new[] { PluralCategory.Other }));
            Assert.That(PluralRules.IntegerCategories("ar").Count, Is.EqualTo(6));
        }

        [Test]
        public void Suffix_RoundTrips()
        {
            foreach (PluralCategory category in new[]
                     {
                         PluralCategory.Zero, PluralCategory.One, PluralCategory.Two,
                         PluralCategory.Few, PluralCategory.Many, PluralCategory.Other
                     })
            {
                Assert.That(PluralRules.TryParseSuffix(PluralRules.Suffix(category), out PluralCategory parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(category));
            }

            Assert.That(PluralRules.TryParseSuffix("several", out _), Is.False);
        }
    }
}
