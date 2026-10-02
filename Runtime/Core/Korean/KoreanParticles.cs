using System.Text;

namespace RetrofitLocalization
{
    /// <summary>
    /// Picks the right Korean particle after a value that is only known at run time.
    ///
    /// Korean particles come in pairs and the choice depends on whether the preceding syllable ends
    /// in a consonant (a final consonant, "batchim"): 검 + 을, but 사과 + 를. A translator cannot know
    /// that for an inserted name, so the convention is to write both forms, "{0}을(를)", and resolve
    /// the pair once the value is in place.
    ///
    /// A precomposed Hangul syllable is U+AC00 + (initial * 21 + medial) * 28 + final, so
    /// (code - 0xAC00) % 28 is the final consonant index, with 0 meaning "no final consonant".
    ///
    /// The direction particle is the one exception: 으로 follows a final consonant and 로 follows a
    /// vowel, but a syllable ending in ㄹ (final index 8) also takes 로: 서울로, never 서울으로.
    ///
    /// When the preceding character is not Hangul (a Latin name, a digit) the written pair is kept,
    /// which is the usual readable convention.
    /// </summary>
    public static class KoreanParticles
    {
        private const char FirstSyllable = '\uAC00';
        private const char LastSyllable = '\uD7A3';
        private const int FinalsPerMedial = 28;
        private const int RieulFinal = 8;

        private readonly struct Pair
        {
            public readonly string Written;      // the two-form marker as a translator writes it
            public readonly string AfterFinal;   // form used after a final consonant
            public readonly string AfterVowel;   // form used after a vowel
            public readonly bool RieulAsVowel;   // true for the direction particle

            public Pair(string written, string afterFinal, string afterVowel, bool rieulAsVowel = false)
            {
                Written = written;
                AfterFinal = afterFinal;
                AfterVowel = afterVowel;
                RieulAsVowel = rieulAsVowel;
            }
        }

        // Both spellings of each pair are accepted, whichever form the translator put first.
        private static readonly Pair[] Pairs =
        {
            new Pair("은(는)", "은", "는"), new Pair("는(은)", "은", "는"),         // topic
            new Pair("이(가)", "이", "가"), new Pair("가(이)", "이", "가"),         // subject
            new Pair("을(를)", "을", "를"), new Pair("를(을)", "을", "를"),         // object
            new Pair("과(와)", "과", "와"), new Pair("와(과)", "과", "와"),         // "and" / "with"
            new Pair("으로(로)", "으로", "로", true), new Pair("로(으로)", "으로", "로", true),
            new Pair("(으)로", "으로", "로", true)                               // direction / means
        };

        /// <summary>
        /// Replaces every two-form particle marker in <paramref name="text"/> with the form that
        /// matches the character before it. Text without a marker is returned unchanged.
        /// </summary>
        public static string Resolve(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('(') < 0) return text;

            StringBuilder output = null;
            int copiedUpTo = 0;
            int i = 0;
            while (i < text.Length)
            {
                if (!TryMatchPair(text, i, out Pair pair))
                {
                    i++;
                    continue;
                }

                int end = i + pair.Written.Length;
                if (TryGetFinalConsonant(text, i - 1, out int final))
                {
                    if (output == null) output = new StringBuilder(text.Length);
                    output.Append(text, copiedUpTo, i - copiedUpTo);
                    bool afterVowel = final == 0 || (pair.RieulAsVowel && final == RieulFinal);
                    output.Append(afterVowel ? pair.AfterVowel : pair.AfterFinal);
                    copiedUpTo = end;
                }

                i = end;
            }

            if (output == null) return text;
            output.Append(text, copiedUpTo, text.Length - copiedUpTo);
            return output.ToString();
        }

        private static bool TryMatchPair(string text, int index, out Pair pair)
        {
            for (int p = 0; p < Pairs.Length; p++)
            {
                string written = Pairs[p].Written;
                if (text[index] == written[0] && string.CompareOrdinal(text, index, written, 0, written.Length) == 0)
                {
                    pair = Pairs[p];
                    return true;
                }
            }

            pair = default;
            return false;
        }

        /// <summary>
        /// Finds the final consonant of the syllable the particle attaches to, looking back from
        /// <paramref name="index"/>. Rich-text tags and closing quotes are silent, so they are skipped:
        /// the particle after "&lt;b&gt;검&lt;/b&gt;" still agrees with 검.
        /// </summary>
        private static bool TryGetFinalConsonant(string text, int index, out int final)
        {
            final = 0;
            int i = index;
            while (i >= 0)
            {
                char c = text[i];
                if (c == '>')
                {
                    int open = text.LastIndexOf('<', i);
                    if (open < 0) break;
                    i = open - 1;
                    continue;
                }

                if (IsClosingQuote(c))
                {
                    i--;
                    continue;
                }

                break;
            }

            if (i < 0) return false;

            char syllable = text[i];
            if (syllable < FirstSyllable || syllable > LastSyllable) return false;

            final = (syllable - FirstSyllable) % FinalsPerMedial;
            return true;
        }

        private static bool IsClosingQuote(char c)
        {
            return c == '\'' || c == '"' || c == '\u2019' || c == '\u201D' || c == '\u300D' || c == '\u300F';
        }
    }
}
