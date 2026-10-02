using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    public class PlaceholdersTests
    {
        [Test]
        public void Slots_AreDistinctAndAscending()
        {
            Assert.That(Placeholders.Slots("{1} beats {0}, then {1} again"), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Slots_IncludeAlignmentAndFormatSpecifiers()
        {
            Assert.That(Placeholders.Slots("{0:N0} of {1,-8} ({2,6:F1})"), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void Slots_IgnoreEscapedBraces()
        {
            Assert.That(Placeholders.Slots("{{0}} is literal, {0} is a slot"), Is.EqualTo(new[] { 0 }));
            Assert.That(Placeholders.Slots("no slots {{here}}"), Is.Empty);
            Assert.That(Placeholders.Slots(null), Is.Empty);
        }

        [TestCase("{name} is not numeric")]
        [TestCase("an unclosed {0")]
        [TestCase("a lone } brace")]
        [TestCase("{0:{1}}")]
        public void MalformedBraces_AreDetected(string format)
        {
            Assert.That(Placeholders.TryGetSlots(format, new List<int>()), Is.False);
        }

        [Test]
        public void WellFormedText_PassesTheBraceCheck()
        {
            Assert.That(Placeholders.TryGetSlots("{0} and {{1}}", new List<int>()), Is.True);
            Assert.That(Placeholders.TryGetSlots(string.Empty, new List<int>()), Is.True);
        }

        [Test]
        public void Compare_MatchingSlots_IsAMatchInAnyOrder()
        {
            PlaceholderParity parity = Placeholders.Compare("{0} gives {1} to {2}", "{2} gets {1} from {0}");
            Assert.That(parity.IsMatch, Is.True);
        }

        [Test]
        public void Compare_ReportsASlotTheSourceNeverHad()
        {
            PlaceholderParity parity = Placeholders.Compare("Page {0}", "Seite {0} von {1}");
            Assert.That(parity.Extra, Is.EqualTo(new[] { 1 }));
            Assert.That(parity.Dropped, Is.Empty);
            Assert.That(parity.IsMatch, Is.False);
        }

        [Test]
        public void Compare_ReportsASlotTheTranslationLost()
        {
            PlaceholderParity parity = Placeholders.Compare("{0} against {1}", "nur {1}");
            Assert.That(parity.Dropped, Is.EqualTo(new[] { 0 }));
            Assert.That(parity.Extra, Is.Empty);
        }

        [Test]
        public void Compare_FlagsMalformedTranslations()
        {
            PlaceholderParity parity = Placeholders.Compare("Page {0}", "Seite {0");
            Assert.That(parity.WellFormed, Is.False);
            Assert.That(parity.IsMatch, Is.False);
        }

        [Test]
        public void TryFormat_FillsTheSlots()
        {
            bool ok = Placeholders.TryFormat(CultureInfo.InvariantCulture, "{0} against {1}", new object[] { 3, 10 }, out string text);
            Assert.That(ok, Is.True);
            Assert.That(text, Is.EqualTo("3 against 10"));
        }

        [Test]
        public void TryFormat_WithTooFewArguments_ReturnsTheTemplateInsteadOfThrowing()
        {
            bool ok = Placeholders.TryFormat(CultureInfo.InvariantCulture, "{0} against {1}", new object[] { 3 }, out string text);
            Assert.That(ok, Is.False);
            Assert.That(text, Is.EqualTo("{0} against {1}"));
        }

        [Test]
        public void TryFormat_WithMalformedBraces_ReturnsTheTemplateInsteadOfThrowing()
        {
            bool ok = Placeholders.TryFormat(CultureInfo.InvariantCulture, "broken {0", new object[] { 3 }, out string text);
            Assert.That(ok, Is.False);
            Assert.That(text, Is.EqualTo("broken {0"));
        }

        [Test]
        public void TryFormat_WithoutArguments_ReturnsTheTextAsIs()
        {
            bool ok = Placeholders.TryFormat(CultureInfo.InvariantCulture, "plain {0}", null, out string text);
            Assert.That(ok, Is.True);
            Assert.That(text, Is.EqualTo("plain {0}"));
        }
    }

    public class InlineTokensTests
    {
        [Test]
        public void Replace_PutsTheWordInPlaceOfTheToken()
        {
            Assert.That(InlineTokens.Replace("The lamp $IS lit", "$IS", "is"), Is.EqualTo("The lamp is lit"));
        }

        [Test]
        public void Replace_WithAnEmptyWord_AlsoRemovesTheSpaceBeforeTheToken()
        {
            // Languages without a present-tense copula map the token to nothing.
            Assert.That(InlineTokens.Replace("The lamp $IS lit", "$IS", string.Empty), Is.EqualTo("The lamp lit"));
            Assert.That(InlineTokens.Replace("$IS lit", "$IS", null), Is.EqualTo(" lit"));
        }

        [Test]
        public void Replace_WithoutTheToken_ReturnsTheText()
        {
            Assert.That(InlineTokens.Replace("no token in here", "$IS", "is"), Is.EqualTo("no token in here"));
            Assert.That(InlineTokens.Replace(null, "$IS", "is"), Is.Null);
        }

        [Test]
        public void LowerMidSentence_LowersTheWord()
        {
            Assert.That(InlineTokens.LowerMidSentence("Garden", "en"), Is.EqualTo("garden"));
            Assert.That(InlineTokens.LowerMidSentence("\u0421\u0430\u0434", "ru"), Is.EqualTo("\u0441\u0430\u0434"));
        }

        [Test]
        public void LowerMidSentence_KeepsGermanNounsCapitalised()
        {
            Assert.That(InlineTokens.LowerMidSentence("Garten", "de"), Is.EqualTo("Garten"));
            Assert.That(InlineTokens.LowerMidSentence("Garten", "de-AT"), Is.EqualTo("Garten"));
        }

        [Test]
        public void LowerMidSentence_LeavesCaselessScriptsAlone()
        {
            Assert.That(InlineTokens.LowerMidSentence("\uC815\uC6D0", "ko"), Is.EqualTo("\uC815\uC6D0"));
            Assert.That(InlineTokens.LowerMidSentence(null, "en"), Is.Null);
        }
    }
}
