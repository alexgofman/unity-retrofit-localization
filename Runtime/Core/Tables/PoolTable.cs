using System;
using System.Collections.Generic;

namespace RetrofitLocalization
{
    /// <summary>
    /// The translated string arrays of one pool table in one locale. Arrays are kept exactly as
    /// written in the file, blanks included, so an audit can still see what is missing.
    /// </summary>
    public sealed class PoolTable
    {
        private readonly Dictionary<string, string[]> _pools;

        public string Name { get; }

        public string Locale { get; }

        public int Count => _pools.Count;

        public IEnumerable<string> Keys => _pools.Keys;

        public PoolTable(string name, string locale, IDictionary<string, string[]> pools)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Locale = locale ?? throw new ArgumentNullException(nameof(locale));
            _pools = pools == null
                ? new Dictionary<string, string[]>(StringComparer.Ordinal)
                : new Dictionary<string, string[]>(pools, StringComparer.Ordinal);
        }

        /// <summary>Parses the contents of a pool table file. Throws <see cref="TableFormatException"/>.</summary>
        public static PoolTable Parse(string name, string locale, string json)
        {
            return new PoolTable(name, locale, TableJson.ParsePools(json));
        }

        public bool TryGet(string key, out string[] values)
        {
            if (key == null)
            {
                values = null;
                return false;
            }

            return _pools.TryGetValue(key, out values);
        }
    }
}
