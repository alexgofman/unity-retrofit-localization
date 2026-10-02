using System.Collections.Generic;

namespace RetrofitLocalization
{
    /// <summary>
    /// Collects displayed strings that look like interface labels but have no entry in the phrase
    /// table, so the gaps can be harvested from a development build and translated in a later pass.
    ///
    /// A sweep sees every string on screen, most of which are not labels at all: names, numbers,
    /// sentences that come from already translated content. The filter keeps only short strings
    /// without digits, format slots or sentence punctuation.
    /// </summary>
    public sealed class UntranslatedPhraseLog
    {
        private const int MinLength = 2;
        private const int MaxLength = 40;
        private const int MaxSpaces = 4;

        private readonly HashSet<string> _phrases = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>True when a phrase was added since <see cref="MarkSaved"/> was last called.</summary>
        public bool IsDirty { get; private set; }

        public int Count => _phrases.Count;

        /// <summary>Records <paramref name="displayed"/> if it looks like a label. Returns true when it is new.</summary>
        public bool Record(string displayed)
        {
            if (displayed == null) return false;
            string phrase = displayed.Trim();
            if (!LooksLikeLabel(phrase) || !_phrases.Add(phrase)) return false;
            IsDirty = true;
            return true;
        }

        /// <summary>The collected phrases in ordinal order.</summary>
        public List<string> ToSortedList()
        {
            var phrases = new List<string>(_phrases);
            phrases.Sort(System.StringComparer.Ordinal);
            return phrases;
        }

        /// <summary>Clears <see cref="IsDirty"/> after the phrases were written somewhere.</summary>
        public void MarkSaved()
        {
            IsDirty = false;
        }

        public void Clear()
        {
            _phrases.Clear();
            IsDirty = false;
        }

        /// <summary>The filter on its own: short, at least two letters, no digits, slots, markup or sentence end.</summary>
        public static bool LooksLikeLabel(string phrase)
        {
            if (phrase == null || phrase.Length < MinLength || phrase.Length > MaxLength) return false;

            int letters = 0;
            int spaces = 0;
            for (int i = 0; i < phrase.Length; i++)
            {
                char c = phrase[i];
                if (char.IsLetter(c)) letters++;
                else if (char.IsDigit(c)) return false;          // dynamic text
                else if (c == ' ') spaces++;
                else if (c == '{' || c == '$' || c == '<') return false; // a template or markup, not a label
            }

            if (letters < 2 || spaces > MaxSpaces) return false;

            char last = phrase[phrase.Length - 1];
            return last != '.' && last != '!' && last != '?'; // a sentence is content, not a label
        }
    }
}
