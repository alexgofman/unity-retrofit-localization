using System;
using System.Collections.Generic;

namespace RetrofitLocalization
{
    /// <summary>The key-to-text table of one locale.</summary>
    public sealed class StringTable
    {
        private readonly Dictionary<string, string> _entries;

        public string Locale { get; }

        public int Count => _entries.Count;

        public IEnumerable<string> Keys => _entries.Keys;

        public StringTable(string locale, IDictionary<string, string> entries)
        {
            Locale = locale ?? throw new ArgumentNullException(nameof(locale));
            _entries = entries == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(entries, StringComparer.Ordinal);
        }

        /// <summary>Parses the contents of a table file. Throws <see cref="TableFormatException"/>.</summary>
        public static StringTable Parse(string locale, string json)
        {
            return new StringTable(locale, TableJson.ParseStrings(json));
        }

        public static StringTable Empty(string locale)
        {
            return new StringTable(locale, null);
        }

        public bool Contains(string key)
        {
            return key != null && _entries.ContainsKey(key);
        }

        public bool TryGet(string key, out string value)
        {
            if (key == null)
            {
                value = null;
                return false;
            }

            return _entries.TryGetValue(key, out value);
        }
    }
}
