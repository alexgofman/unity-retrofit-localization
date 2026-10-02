using System;
using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    /// <summary>
    /// The segmenter is tested with <see cref="BracketShaper"/>, which wraps each run it is given in
    /// square brackets. Expected strings then read: which runs were shaped, in which order they
    /// were emitted, and that every tag is still exactly as it was written.
    /// </summary>
    public class RtlTextTests
    {
        private const string A = Fixtures.Text;
        private const string B = Fixtures.Name;
        private const string C = Fixtures.Gold;

        private BracketShaper _shaper;

        [SetUp]
        public void SetUp()
        {
            _shaper = new BracketShaper();
        }

        private string Apply(string text)
        {
            return RtlText.Apply(text, _shaper);
        }

        [Test]
        public void TextWithoutArabic_IsReturnedAsTheSameInstance()
        {
            const string text = "Level <b>5</b> (done)";

            Assert.That(Apply(text), Is.SameAs(text));
            Assert.That(_shaper.Runs, Is.Empty);
        }

        [Test]
        public void NullAndEmpty_ArePassedThrough()
        {
            Assert.That(Apply(null), Is.Null);
            Assert.That(Apply(string.Empty), Is.Empty);
        }

        [Test]
        public void PlainArabic_GoesToTheShaperAsOneRun()
        {
            Assert.That(Apply(A + " " + B), Is.EqualTo("[" + A + " " + B + "]"));
            Assert.That(_shaper.Runs, Is.EqualTo(new[] { A + " " + B }));
        }

        [Test]
        public void TagsAroundALabel_AreLeftIntact()
        {
            Assert.That(Apply("<color=#FFD54A>" + A + "</color>"), Is.EqualTo("<color=#FFD54A>[" + A + "]</color>"));
        }

        [Test]
        public void TheShaper_NeverSeesATag()
        {
            Apply("<size=120%><b>" + A + "</b> <color=red>" + B + "</color><sprite=2> " + C + "</size>");

            Assert.That(_shaper.Runs.Count, Is.EqualTo(3));
            foreach (string run in _shaper.Runs)
            {
                Assert.That(run.IndexOf('<'), Is.EqualTo(-1), run);
                Assert.That(run.IndexOf('>'), Is.EqualTo(-1), run);
            }
        }

        [Test]
        public void EveryTag_SurvivesVerbatim()
        {
            string shaped = Apply("<size=120%><b>" + A + "</b> <color=red>" + B + "</color><sprite=2> " + C + "</size>");

            foreach (string tag in new[] { "<size=120%>", "<b>", "</b>", "<color=red>", "</color>", "<sprite=2>", "</size>" })
            {
                Assert.That(shaped, Does.Contain(tag));
            }
        }

        [Test]
        public void RunsOfALine_AreEmittedRightToLeft_AndTagPairsSwapPlaces()
        {
            // Typed order:  A <b>B</b> C     Visual order:  C <b>B</b> A
            Assert.That(Apply(A + " <b>" + B + "</b> " + C), Is.EqualTo("[ " + C + "]<b>[" + B + "]</b>[" + A + " ]"));
        }

        [Test]
        public void NestedTags_StayNested()
        {
            // <b>A <i>B</i></b>: B is bold and italic, A is bold only, before and after.
            Assert.That(Apply("<b>" + A + " <i>" + B + "</i></b>"), Is.EqualTo("<b><i>[" + B + "]</i>[" + A + " ]</b>"));
        }

        [Test]
        public void StandaloneTag_KeepsItsPlaceBetweenTheRuns()
        {
            Assert.That(Apply(A + "<sprite=3>" + B), Is.EqualTo("[" + B + "]<sprite=3>[" + A + "]"));
            Assert.That(Apply(A + "<sprite name=\"coin\"/>" + B), Is.EqualTo("[" + B + "]<sprite name=\"coin\"/>[" + A + "]"));
        }

        [Test]
        public void NumberBetweenTags_IsNotShaped()
        {
            Assert.That(Apply(A + " <b>42</b>"), Is.EqualTo("<b>42</b>[" + A + " ]"));
            Assert.That(_shaper.Runs, Is.EqualTo(new[] { A + " " }));
        }

        [Test]
        public void PunctuationBetweenTags_FollowsTheLineDirection_AndBracketsAreMirrored()
        {
            // Typed order:  <b>A</b> (<i>B</i>)     Visual order:  (<i>B</i>) <b>A</b>
            Assert.That(Apply("<b>" + A + "</b> (<i>" + B + "</i>)"),
                Is.EqualTo("(<i>[" + B + "]</i>) <b>[" + A + "]</b>"));
        }

        [Test]
        public void ColourShorthand_PairsWithTheColourClosingTag()
        {
            Assert.That(Apply("<#FF0000>" + A + "</color> " + B), Is.EqualTo("[ " + B + "]<#FF0000>[" + A + "]</color>"));
        }

        [Test]
        public void TagNames_AreMatchedWithoutRegardToCase()
        {
            Assert.That(Apply("<B>" + A + "</b> " + B), Is.EqualTo("[ " + B + "]<B>[" + A + "]</b>"));
        }

        [Test]
        public void UnmatchedClosingTag_KeepsItsPlace()
        {
            Assert.That(Apply(A + "</b>" + B), Is.EqualTo("[" + B + "]</b>[" + A + "]"));
        }

        [Test]
        public void TagLeftOpen_IsClosedAtTheEndOfTheLine()
        {
            // Without the closing tag the opening tag would end up after the text it applies to.
            Assert.That(Apply("<b>" + A), Is.EqualTo("<b>[" + A + "]</b>"));
        }

        [Test]
        public void Lines_KeepTheirOrder_AndAreReorderedOneByOne()
        {
            Assert.That(Apply(A + " <b>" + B + "</b>\n" + C), Is.EqualTo("<b>[" + B + "]</b>[" + A + " ]\n[" + C + "]"));
        }

        [Test]
        public void LineBreakStyles_ArePreserved()
        {
            Assert.That(Apply(A + "\r\n" + B), Is.EqualTo("[" + A + "]\r\n[" + B + "]"));
            Assert.That(Apply(A + "<br>" + B), Is.EqualTo("[" + A + "]<br>[" + B + "]"));
            Assert.That(Apply(A + "\n\n" + B), Is.EqualTo("[" + A + "]\n\n[" + B + "]"));
        }

        [Test]
        public void LeftToRightLine_InsideArabicText_IsCopiedVerbatim()
        {
            Assert.That(Apply("<b>Title</b> (1)\n" + A), Is.EqualTo("<b>Title</b> (1)\n[" + A + "]"));
        }

        [Test]
        public void TagSpanningALineBreak_IsClosedAndReopened()
        {
            Assert.That(Apply("<color=red>" + A + "\n" + B + "</color>"),
                Is.EqualTo("<color=red>[" + A + "]</color>\n<color=red>[" + B + "]</color>"));
        }

        [Test]
        public void TagSpanningALineBreak_IsAlsoBalancedOnLeftToRightLines()
        {
            Assert.That(Apply("<color=red>Title\n" + A + "</color>"),
                Is.EqualTo("<color=red>Title</color>\n<color=red>[" + A + "]</color>"));
        }

        [Test]
        public void AngleBracketThatIsNotATag_IsOrdinaryText()
        {
            string text = "1 < 2 > 0 " + A;

            Assert.That(Apply(text), Is.EqualTo("[" + text + "]"));
        }

        [Test]
        public void AlreadyShapedText_IsLeftAlone()
        {
            string once = RtlText.Apply("<b>" + Fixtures.Hello + "</b> " + Fixtures.Book + " 3", BasicArabicShaper.Instance);
            string twice = RtlText.Apply(once, BasicArabicShaper.Instance);

            Assert.That(once, Is.Not.EqualTo("<b>" + Fixtures.Hello + "</b> " + Fixtures.Book + " 3"));
            Assert.That(twice, Is.SameAs(once));
        }

        [Test]
        public void ShaperIsRequired()
        {
            Assert.Throws<ArgumentNullException>(() => RtlText.Apply(A, null));
        }

        [Test]
        public void IsArabicLetter_CountsLettersOnly()
        {
            Assert.That(RtlText.IsArabicLetter('\u0628'), Is.True);  // beh
            Assert.That(RtlText.IsArabicLetter('\u067E'), Is.True);  // peh, a Persian letter
            Assert.That(RtlText.IsArabicLetter('\u0640'), Is.False); // tatweel
            Assert.That(RtlText.IsArabicLetter('\u064E'), Is.False); // fatha, a vowel mark
            Assert.That(RtlText.IsArabicLetter('\u0663'), Is.False); // Arabic-Indic digit three
            Assert.That(RtlText.IsArabicLetter('\u061F'), Is.False); // Arabic question mark
            Assert.That(RtlText.IsArabicLetter('\uFE8F'), Is.False); // beh, isolated presentation form
            Assert.That(RtlText.IsArabicLetter('a'), Is.False);
        }

        [Test]
        public void Mirror_SwapsBracketPairs()
        {
            Assert.That(RtlText.Mirror('('), Is.EqualTo(')'));
            Assert.That(RtlText.Mirror(']'), Is.EqualTo('['));
            Assert.That(RtlText.Mirror('\u00AB'), Is.EqualTo('\u00BB'));
            Assert.That(RtlText.Mirror('x'), Is.EqualTo('x'));
        }
    }
}
