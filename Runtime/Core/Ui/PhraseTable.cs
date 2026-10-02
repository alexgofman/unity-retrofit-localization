using System;
using System.Collections.Generic;

namespace RetrofitLocalization
{
    /// <summary>
    /// A translation table keyed by the source-language text itself rather than by an id.
    ///
    /// This is what makes a retrofit possible without touching call sites: whatever string a label
    /// currently shows is looked up as-is, wherever it came from (a prefab, a scene, a literal in
    /// code). A string that is not in the table is simply left alone.
    /// </summary>
    public sealed class PhraseTable
    {
        private readonly Dictionary<string, string> _phrases;

        public string Locale { get; }

        public int Count => _phrases.Count;

        public IEnumerable<string> Keys => _phrases.Keys;

        public PhraseTable(string locale, IDictionary<string, string> phrases)
        {
            Locale = locale ?? throw new ArgumentNullException(nameof(locale));
            _phrases = new Dictionary<string, string>(StringComparer.Ordinal);
            if (phrases == null) return;

            foreach (KeyValuePair<string, string> pair in phrases)
            {
                // A blank translation, or one equal to its source, would change nothing on screen.
                if (string.IsNullOrEmpty(pair.Value) || string.Equals(pair.Key, pair.Value, StringComparison.Ordinal))
                    continue;
                _phrases[pair.Key] = pair.Value;
            }
        }

        /// <summary>Parses the contents of a phrase table file. Throws <see cref="TableFormatException"/>.</summary>
        public static PhraseTable Parse(string locale, string json)
        {
            return new PhraseTable(locale, TableJson.ParseStrings(json));
        }

        /// <summary>
        /// Looks up the whole of <paramref name="displayed"/>, ignoring surrounding white space, and
        /// puts that white space back around the translation. Matching is exact: a label built by
        /// joining two phrases matches neither of them, so such call sites have to translate each
        /// piece before joining.
        /// </summary>
        public bool TryTranslate(string displayed, out string translated)
        {
            translated = null;
            if (string.IsNullOrEmpty(displayed)) return false;

            if (_phrases.TryGetValue(displayed, out translated)) return true;

            string phrase = displayed.Trim();
            if (phrase.Length == 0 || phrase.Length == displayed.Length) return false;
            if (!_phrases.TryGetValue(phrase, out string value)) return false;

            int leading = displayed.Length - displayed.TrimStart().Length;
            translated = displayed.Substring(0, leading) + value + displayed.Substring(leading + phrase.Length);
            return true;
        }
    }
}
