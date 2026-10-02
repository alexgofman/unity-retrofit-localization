using System.Globalization;
using System.Text;

namespace RetrofitLocalization
{
    /// <summary>
    /// A small built-in shaper, so Arabic is readable without any third-party library.
    ///
    /// It does two things. First it joins: every letter of the standard Arabic alphabet
    /// (U+0621 to U+064A) is replaced by its isolated, initial, medial or final presentation form
    /// depending on its neighbours, and lam followed by alef becomes the mandatory lam-alef
    /// ligature. Vowel marks are transparent to joining and stay attached to their letter.
    /// Then it reorders for a left-to-right renderer: the line is reversed, while numbers and
    /// left-to-right words keep their own internal order and brackets are mirrored.
    ///
    /// It is a fallback, not a full implementation. The extra letters of Persian, Urdu and other
    /// languages written in Arabic script are passed through unjoined, and the reordering is a
    /// simplification of the Unicode bidirectional algorithm. For production Arabic, plug a
    /// complete shaping library in through <see cref="IRtlShaper"/>.
    /// </summary>
    public sealed class BasicArabicShaper : IRtlShaper
    {
        public static readonly BasicArabicShaper Instance = new BasicArabicShaper();

        private const char FirstLetter = '\u0621';
        private const char LastLetter = '\u064A';
        private const char Lam = '\u0644';
        private const char Tatweel = '\u0640';

        private enum Joining : byte
        {
            None,       // joins to nothing: hamza, and every character that is not an Arabic letter
            Right,      // joins to the previous letter only: alef, dal, reh, waw ...
            Dual,       // joins on both sides: beh, seen, lam, meem ...
            Causing,    // tatweel: has no forms of its own but makes its neighbours join
            Transparent // vowel marks: skipped when looking for a letter's neighbours
        }

        // Isolated presentation form of each letter from U+0621 to U+064A (0 = the letter has none).
        // The other forms follow it directly in Unicode: final = isolated + 1, and for dual-joining
        // letters initial = isolated + 2 and medial = isolated + 3.
        private static readonly char[] Isolated =
        {
            '\uFE80', // 0621 hamza
            '\uFE81', // 0622 alef with madda above
            '\uFE83', // 0623 alef with hamza above
            '\uFE85', // 0624 waw with hamza above
            '\uFE87', // 0625 alef with hamza below
            '\uFE89', // 0626 yeh with hamza above
            '\uFE8D', // 0627 alef
            '\uFE8F', // 0628 beh
            '\uFE93', // 0629 teh marbuta
            '\uFE95', // 062A teh
            '\uFE99', // 062B theh
            '\uFE9D', // 062C jeem
            '\uFEA1', // 062D hah
            '\uFEA5', // 062E khah
            '\uFEA9', // 062F dal
            '\uFEAB', // 0630 thal
            '\uFEAD', // 0631 reh
            '\uFEAF', // 0632 zain
            '\uFEB1', // 0633 seen
            '\uFEB5', // 0634 sheen
            '\uFEB9', // 0635 sad
            '\uFEBD', // 0636 dad
            '\uFEC1', // 0637 tah
            '\uFEC5', // 0638 zah
            '\uFEC9', // 0639 ain
            '\uFECD', // 063A ghain
            '\0', '\0', '\0', '\0', '\0', // 063B-063F: letters of other languages, no presentation forms
            '\0',     // 0640 tatweel
            '\uFED1', // 0641 feh
            '\uFED5', // 0642 qaf
            '\uFED9', // 0643 kaf
            '\uFEDD', // 0644 lam
            '\uFEE1', // 0645 meem
            '\uFEE5', // 0646 noon
            '\uFEE9', // 0647 heh
            '\uFEED', // 0648 waw
            '\uFEEF', // 0649 alef maksura
            '\uFEF1'  // 064A yeh
        };

        private static readonly Joining[] JoiningTypes =
        {
            Joining.None,  // 0621 hamza
            Joining.Right, // 0622
            Joining.Right, // 0623
            Joining.Right, // 0624
            Joining.Right, // 0625
            Joining.Dual,  // 0626
            Joining.Right, // 0627 alef
            Joining.Dual,  // 0628 beh
            Joining.Right, // 0629 teh marbuta
            Joining.Dual,  // 062A
            Joining.Dual,  // 062B
            Joining.Dual,  // 062C
            Joining.Dual,  // 062D
            Joining.Dual,  // 062E
            Joining.Right, // 062F dal
            Joining.Right, // 0630 thal
            Joining.Right, // 0631 reh
            Joining.Right, // 0632 zain
            Joining.Dual,  // 0633
            Joining.Dual,  // 0634
            Joining.Dual,  // 0635
            Joining.Dual,  // 0636
            Joining.Dual,  // 0637
            Joining.Dual,  // 0638
            Joining.Dual,  // 0639
            Joining.Dual,  // 063A
            Joining.None, Joining.None, Joining.None, Joining.None, Joining.None, // 063B-063F
            Joining.Causing, // 0640 tatweel
            Joining.Dual,  // 0641
            Joining.Dual,  // 0642
            Joining.Dual,  // 0643
            Joining.Dual,  // 0644 lam
            Joining.Dual,  // 0645
            Joining.Dual,  // 0646
            Joining.Dual,  // 0647
            Joining.Right, // 0648 waw
            Joining.Right, // 0649 alef maksura
            Joining.Dual   // 064A yeh
        };

        private enum Direction : byte
        {
            RightToLeft, // Arabic letters and everything that simply follows the line direction
            LeftToRight, // letters of left-to-right scripts
            Number,
            Neutral      // spaces, punctuation, symbols
        }

        public string Shape(string logicalRun)
        {
            if (string.IsNullOrEmpty(logicalRun)) return logicalRun;
            return Reorder(Join(logicalRun));
        }

        // ---- Step 1: joining, in logical order -------------------------------------------------

        private static string Join(string text)
        {
            var output = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                char isolated = c >= FirstLetter && c <= LastLetter ? Isolated[c - FirstLetter] : '\0';
                if (isolated == '\0')
                {
                    output.Append(c); // not a letter this shaper has forms for
                    continue;
                }

                // A letter joins the one before it when that letter can join forwards. Hamza joins
                // nothing and always takes its isolated form.
                Joining type = JoiningTypes[c - FirstLetter];
                Joining previous = JoiningOf(NeighbourBefore(text, i));
                bool joinsPrevious = type != Joining.None
                                     && (previous == Joining.Dual || previous == Joining.Causing);

                int nextIndex = IndexOfNeighbourAfter(text, i);
                char next = nextIndex < 0 ? '\0' : text[nextIndex];

                if (c == Lam)
                {
                    char ligature = LamAlefLigature(next);
                    if (ligature != '\0')
                    {
                        // Isolated ligature, or the final form (+1) when the lam joins the letter before it.
                        output.Append((char)(ligature + (joinsPrevious ? 1 : 0)));
                        // Marks written between the lam and the alef stay with the ligature.
                        for (int m = i + 1; m < nextIndex; m++) output.Append(text[m]);
                        i = nextIndex; // the alef is now part of the ligature
                        continue;
                    }
                }

                Joining following = JoiningOf(next);
                bool joinsNext = type == Joining.Dual
                                 && (following == Joining.Dual || following == Joining.Right
                                     || following == Joining.Causing);

                int form = joinsPrevious ? (joinsNext ? 3 : 1) : (joinsNext ? 2 : 0);
                output.Append((char)(isolated + form));
            }

            return output.ToString();
        }

        private static Joining JoiningOf(char c)
        {
            if (c >= FirstLetter && c <= LastLetter) return JoiningTypes[c - FirstLetter];
            return IsArabicMark(c) ? Joining.Transparent : Joining.None;
        }

        private static bool IsArabicMark(char c)
        {
            return (c >= '\u064B' && c <= '\u065F') || c == '\u0670';
        }

        private static char NeighbourBefore(string text, int index)
        {
            for (int i = index - 1; i >= 0; i--)
            {
                if (!IsArabicMark(text[i])) return text[i];
            }

            return '\0';
        }

        private static int IndexOfNeighbourAfter(string text, int index)
        {
            for (int i = index + 1; i < text.Length; i++)
            {
                if (!IsArabicMark(text[i])) return i;
            }

            return -1;
        }

        private static char LamAlefLigature(char alef)
        {
            switch (alef)
            {
                case '\u0622': return '\uFEF5'; // lam + alef with madda above
                case '\u0623': return '\uFEF7'; // lam + alef with hamza above
                case '\u0625': return '\uFEF9'; // lam + alef with hamza below
                case '\u0627': return '\uFEFB'; // lam + alef
                default: return '\0';
            }
        }

        // ---- Step 2: visual order for a left-to-right renderer ---------------------------------

        private static string Reorder(string text)
        {
            int length = text.Length;

            // Group the text into clusters: a character plus the marks that follow it, or a
            // surrogate pair. A cluster is moved as one piece, so a vowel mark still follows the
            // letter it belongs to after the line has been reversed.
            var clusterStart = new int[length + 1];
            var direction = new Direction[length];
            int count = 0;
            for (int i = 0; i < length;)
            {
                int start = i;
                Direction clusterDirection = DirectionOf(text, i);
                i += char.IsHighSurrogate(text[i]) && i + 1 < length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                while (i < length && IsMark(text[i])) i++;
                clusterStart[count] = start;
                direction[count] = clusterDirection;
                count++;
            }

            clusterStart[count] = length;

            // A left-to-right island starts and ends on a letter or digit and includes the neutral
            // characters in between ("Level 5", "10:30"). Neutrals at its edges belong to the
            // right-to-left flow. islandStart[k] is the first cluster of the island that cluster k
            // belongs to, or -1.
            var islandStart = new int[count];
            for (int k = 0; k < count; k++) islandStart[k] = -1;
            for (int k = 0; k < count; k++)
            {
                if (direction[k] != Direction.LeftToRight && direction[k] != Direction.Number) continue;

                int first = k;
                int last = k;
                for (int j = k + 1; j < count && direction[j] != Direction.RightToLeft; j++)
                {
                    if (direction[j] != Direction.Neutral) last = j;
                }

                // A sign or currency symbol in front of a number and a percent sign or unit symbol
                // behind it stay attached to the number.
                if (direction[first] == Direction.Number && first > 0 && IsNumberPrefix(text, clusterStart, first - 1))
                    first--;
                if (direction[last] == Direction.Number && last + 1 < count && IsNumberSuffix(text, clusterStart, last + 1))
                    last++;

                for (int j = first; j <= last; j++) islandStart[j] = first;
                k = last;
            }

            var output = new StringBuilder(length);
            for (int k = count - 1; k >= 0; k--)
            {
                if (islandStart[k] >= 0)
                {
                    int first = islandStart[k];
                    output.Append(text, clusterStart[first], clusterStart[k + 1] - clusterStart[first]);
                    k = first;
                    continue;
                }

                int start = clusterStart[k];
                int clusterLength = clusterStart[k + 1] - start;
                if (clusterLength == 1) output.Append(RtlText.Mirror(text[start]));
                else output.Append(text, start, clusterLength);
            }

            return output.ToString();
        }

        private static Direction DirectionOf(string text, int index)
        {
            char c = text[index];
            if ((c >= '0' && c <= '9') || (c >= '\u0660' && c <= '\u0669') || (c >= '\u06F0' && c <= '\u06F9'))
                return Direction.Number;

            // The Arabic blocks, including the presentation forms produced by the joining step.
            if ((c >= '\u0600' && c <= '\u08FF') || (c >= '\uFB50' && c <= '\uFDFF') || (c >= '\uFE70' && c <= '\uFEFC'))
                return Direction.RightToLeft;

            return char.IsLetter(text, index) ? Direction.LeftToRight : Direction.Neutral;
        }

        private static bool IsMark(char c)
        {
            if (IsArabicMark(c)) return true;
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            return category == UnicodeCategory.NonSpacingMark
                   || category == UnicodeCategory.SpacingCombiningMark
                   || category == UnicodeCategory.EnclosingMark;
        }

        private static bool IsNumberPrefix(string text, int[] clusterStart, int cluster)
        {
            if (clusterStart[cluster + 1] - clusterStart[cluster] != 1) return false;
            char c = text[clusterStart[cluster]];
            return c == '+' || c == '-' || c == '\u2212' || c == '#' || IsCurrency(c);
        }

        private static bool IsNumberSuffix(string text, int[] clusterStart, int cluster)
        {
            if (clusterStart[cluster + 1] - clusterStart[cluster] != 1) return false;
            char c = text[clusterStart[cluster]];
            return c == '%' || c == '\u2030' || c == '\u00B0' || IsCurrency(c);
        }

        private static bool IsCurrency(char c)
        {
            return CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.CurrencySymbol;
        }
    }
}
