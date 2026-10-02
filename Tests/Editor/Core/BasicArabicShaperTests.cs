using System;
using System.Text;
using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    /// <summary>
    /// Expected values are spelled out as presentation-form code points in visual order, i.e. the
    /// last letter of the word first. Forms: isolated, +1 final, +2 initial, +3 medial.
    /// </summary>
    public class BasicArabicShaperTests
    {
        // Shaped, visual-order versions of the fixture words.
        private const string ShapedText = "\uFEBA\uFEE7";               // sad final, noon initial
        private const string ShapedBook = "\uFE8F\uFE8E\uFE98\uFEDB";   // beh isolated, alef final, teh medial, kaf initial

        private static string Shape(string text)
        {
            return BasicArabicShaper.Instance.Shape(text);
        }

        [Test]
        public void Letters_TakeTheFormTheirNeighboursCallFor()
        {
            // meem initial, reh final, hah initial (reh does not join forwards), beh medial, alef final
            Assert.That(Shape(Fixtures.Hello), Is.EqualTo("\uFE8E\uFE92\uFEA3\uFEAE\uFEE3"));
            Assert.That(Shape(Fixtures.Book), Is.EqualTo(ShapedBook));
            Assert.That(Shape(Fixtures.Text), Is.EqualTo(ShapedText));
            // alef isolated, seen initial, meem final
            Assert.That(Shape(Fixtures.Name), Is.EqualTo("\uFEE2\uFEB3\uFE8D"));
            // thal isolated, heh initial, beh final
            Assert.That(Shape(Fixtures.Gold), Is.EqualTo("\uFE90\uFEEB\uFEAB"));
        }

        [Test]
        public void LamFollowedByAlef_BecomesOneLigature()
        {
            // lam + alef on their own: the isolated ligature
            Assert.That(Shape("\u0644\u0627"), Is.EqualTo("\uFEFB"));
            // seen initial, lam-alef final ligature, meem isolated (alef does not join forwards)
            Assert.That(Shape(Fixtures.Peace), Is.EqualTo("\uFEE1\uFEFC\uFEB3"));
            // lam + alef with hamza above
            Assert.That(Shape("\u0644\u0623"), Is.EqualTo("\uFEF7"));
        }

        [Test]
        public void Hamza_JoinsNothing()
        {
            // meem initial, alef final, hamza isolated
            Assert.That(Shape(Fixtures.Water), Is.EqualTo("\uFE80\uFE8E\uFEE3"));
        }

        [Test]
        public void SingleLetter_IsIsolated()
        {
            Assert.That(Shape("\u0628"), Is.EqualTo("\uFE8F"));
            Assert.That(Shape("\u0627"), Is.EqualTo("\uFE8D"));
        }

        [Test]
        public void Tatweel_MakesItsNeighboursJoin()
        {
            // beh + tatweel + beh: initial, tatweel, final
            Assert.That(Shape("\u0628\u0640\u0628"), Is.EqualTo("\uFE90\u0640\uFE91"));
        }

        [Test]
        public void VowelMarks_AreTransparentToJoining_AndStayWithTheirLetter()
        {
            // meem + fatha + reh: the meem still joins the reh, and the fatha still follows the meem.
            Assert.That(Shape("\u0645\u064E\u0631"), Is.EqualTo("\uFEAE\uFEE3\u064E"));
            // lam + fatha + alef: one ligature carrying the mark.
            Assert.That(Shape("\u0644\u064E\u0627"), Is.EqualTo("\uFEFB\u064E"));
        }

        [Test]
        public void Numbers_KeepTheirDigitOrder()
        {
            Assert.That(Shape(Fixtures.Book + " 37"), Is.EqualTo("37 " + ShapedBook));
            Assert.That(Shape("37 " + Fixtures.Book), Is.EqualTo(ShapedBook + " 37"));
            // Arabic-Indic digits three and four
            Assert.That(Shape(Fixtures.Book + " \u0663\u0664"), Is.EqualTo("\u0663\u0664 " + ShapedBook));
        }

        [Test]
        public void LeftToRightPhrase_KeepsItsOwnOrder()
        {
            Assert.That(Shape(Fixtures.Text + " Level 5 " + Fixtures.Text),
                Is.EqualTo(ShapedText + " Level 5 " + ShapedText));
        }

        [Test]
        public void PunctuationAfterALeftToRightWord_FollowsTheLineDirection()
        {
            // As in any right-to-left paragraph, the trailing mark lands on the left of the word.
            Assert.That(Shape(Fixtures.Text + " Unity!"), Is.EqualTo("!Unity " + ShapedText));
        }

        [Test]
        public void SignsAndUnits_StayAttachedToTheirNumber()
        {
            Assert.That(Shape(Fixtures.Text + " 50%"), Is.EqualTo("50% " + ShapedText));
            Assert.That(Shape(Fixtures.Text + " -5"), Is.EqualTo("-5 " + ShapedText));
            Assert.That(Shape(Fixtures.Text + " $20"), Is.EqualTo("$20 " + ShapedText));
            Assert.That(Shape(Fixtures.Text + " 10:30"), Is.EqualTo("10:30 " + ShapedText));
        }

        [Test]
        public void Brackets_AreMirrored_SoTheyStillEncloseTheText()
        {
            Assert.That(Shape("(" + Fixtures.Text + ")"), Is.EqualTo("(" + ShapedText + ")"));
            Assert.That(Shape(Fixtures.Book + " [" + Fixtures.Text + "]"), Is.EqualTo("[" + ShapedText + "] " + ShapedBook));
        }

        [Test]
        public void TextWithoutArabic_IsNotScrambled()
        {
            // The segmenter only hands over runs that contain Arabic, but a left-to-right word
            // passed on its own still has to come back readable.
            Assert.That(Shape("abc"), Is.EqualTo("abc"));
            Assert.That(Shape(string.Empty), Is.Empty);
            Assert.That(Shape(null), Is.Null);
        }

        [Test]
        public void SurrogatePairs_AreNotSplit()
        {
            const string emoji = "\uD83D\uDE00";
            Assert.That(Shape(Fixtures.Text + " " + emoji), Is.EqualTo(emoji + " " + ShapedText));
        }

        // Words covering every joining situation: right-joining letters, hamza carriers, teh marbuta,
        // alef maksura and all four lam-alef ligatures.
        private static readonly string[] Words =
        {
            Fixtures.Hello, Fixtures.Peace, Fixtures.Book, Fixtures.Water, Fixtures.Text, Fixtures.Name, Fixtures.Gold,
            "\u0645\u062F\u0631\u0633\u0629",                         // school
            "\u0627\u0644\u0644\u063A\u0629",                         // the language
            "\u0625\u0639\u062F\u0627\u062F\u0627\u062A",             // settings
            "\u0645\u0648\u0627\u0641\u0642",                         // agree
            "\u0625\u0644\u063A\u0627\u0621",                         // cancel
            "\u0631\u062C\u0648\u0639",                               // return
            "\u0627\u0644\u0639\u0631\u0628\u064A\u0629",             // Arabic
            "\u0623\u0647\u0644\u0627",                               // welcome
            "\u0627\u0644\u0635\u0648\u062A",                         // the sound
            "\u0634\u064A\u0621",                                     // thing
            "\u0627\u0644\u0622\u0646",                               // now
            "\u0644\u0623\u0646",                                     // because
            "\u0627\u0644\u0625\u0639\u062F\u0627\u062F\u0627\u062A", // the settings
            "\u0645\u0633\u0624\u0648\u0644",                         // responsible
            "\u0642\u0627\u0626\u0645\u0629",                         // list
            "\u0645\u0633\u062A\u0648\u0649",                         // level
            "\u0638\u0644",                                           // shade
            "\u0636\u0648\u0621",                                     // light
            "\u062B\u0644\u062C",                                     // snow
            "\u062E\u0637",                                           // line
            "\u0632\u0631",                                           // button
            "\u0634\u0643\u0631\u0627"                                // thanks
        };

        [Test]
        public void ShapedWords_ContainNoPlainLettersAnyMore()
        {
            foreach (string word in Words)
            {
                Assert.That(RtlText.ContainsArabicLetters(Shape(word)), Is.False, word);
            }
        }

        [Test]
        public void ShapedWords_NormaliseBackToTheOriginalSpelling()
        {
            // Compatibility normalisation maps every presentation form back to its base letter and
            // every lam-alef ligature back to lam + alef. Undoing the reversal first, the result has
            // to be the word that went in, which checks the form table independently of the tests above.
            foreach (string word in Words)
            {
                char[] visual = Shape(word).ToCharArray();
                Array.Reverse(visual);
                string logical = new string(visual).Normalize(NormalizationForm.FormKC);

                Assert.That(logical, Is.EqualTo(word), word);
            }
        }

        [Test]
        public void EveryLetterWithForms_HasAnIsolatedFormThatNormalisesBack()
        {
            for (char letter = '\u0621'; letter <= '\u064A'; letter++)
            {
                if (letter >= '\u063B' && letter <= '\u0640') continue; // no presentation forms

                string shaped = Shape(letter.ToString());

                Assert.That(shaped.Length, Is.EqualTo(1));
                Assert.That(shaped[0], Is.GreaterThanOrEqualTo('\uFE80'));
                Assert.That(shaped.Normalize(NormalizationForm.FormKC), Is.EqualTo(letter.ToString()));
            }
        }
    }
}
