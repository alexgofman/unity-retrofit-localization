using System;

namespace RetrofitLocalization
{
    /// <summary>
    /// Helpers for codebases that build sentences from templates with their own tokens
    /// ("$SUBJECT is here") rather than numbered slots.
    /// </summary>
    public static class InlineTokens
    {
        /// <summary>
        /// Replaces <paramref name="token"/> with <paramref name="word"/>.
        ///
        /// Some languages have no word at all where English needs one: the present-tense copula
        /// ("am", "are") is simply absent in Russian, Turkish, Arabic, Korean, Japanese, Chinese and
        /// Thai. A translator expresses that by mapping the token to an empty string, and then the
        /// space in front of the token has to go too, or the sentence keeps a double space.
        /// </summary>
        public static string Replace(string text, string token, string word)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token)) return text;

            if (string.IsNullOrEmpty(word))
            {
                return text.Replace(" " + token, string.Empty).Replace(token, string.Empty);
            }

            return text.Replace(token, word);
        }

        /// <summary>
        /// Lower-cases a word that is about to be embedded in the middle of a sentence.
        ///
        /// German is the exception: nouns keep their capital letter wherever they stand. Scripts
        /// without letter case (Chinese, Japanese, Korean, Arabic, Thai) are unaffected.
        /// </summary>
        public static string LowerMidSentence(string word, string locale)
        {
            if (string.IsNullOrEmpty(word)) return word;
            if (IsLanguage(locale, "de")) return word;
            return word.ToLowerInvariant();
        }

        internal static bool IsLanguage(string locale, string language)
        {
            if (string.IsNullOrEmpty(locale)) return false;
            if (!locale.StartsWith(language, StringComparison.OrdinalIgnoreCase)) return false;
            return locale.Length == language.Length || locale[language.Length] == '-' || locale[language.Length] == '_';
        }
    }
}
