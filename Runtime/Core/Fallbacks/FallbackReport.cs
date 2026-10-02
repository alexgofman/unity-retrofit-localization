using System;

namespace RetrofitLocalization
{
    /// <summary>Every way a lookup can end up showing something other than the requested translation.</summary>
    public enum FallbackKind
    {
        /// <summary>A locale code that is not in the catalog was requested.</summary>
        UnknownLocale,

        /// <summary>The locale has no string table file at all.</summary>
        MissingTable,

        /// <summary>A table file exists but could not be parsed; it is treated as empty.</summary>
        MalformedTable,

        /// <summary>The key is missing in the active locale; the source-language text is shown.</summary>
        MissingKey,

        /// <summary>The key is in no table; the raw key (or the caller's default text) is shown.</summary>
        UnknownKey,

        /// <summary>The text could not be formatted with the given arguments; it is shown unformatted.</summary>
        FormatError,

        /// <summary>The plural form for this count is missing; the "other" form is shown.</summary>
        MissingPluralForm,

        /// <summary>A phrase asked for by source text has no translation; the source text is shown.</summary>
        MissingPhrase,

        /// <summary>A pool table has no file for the active locale; all of its pools stay in the source language.</summary>
        MissingPoolTable,

        /// <summary>The pool table exists but lacks this pool; the pool stays in the source language.</summary>
        MissingPool,

        /// <summary>
        /// The translated pool has a different number of entries. Pools are often indexed in parallel
        /// (titles and bodies), so a partial array is refused and the whole pool stays in the source language.
        /// </summary>
        PoolLengthMismatch,

        /// <summary>One entry of a translated pool is blank; that entry stays in the source language.</summary>
        EmptyPoolElement,

        /// <summary>A per-locale variant of an asset is missing; the source-language asset is used.</summary>
        MissingAssetVariant
    }

    /// <summary>
    /// One fallback, with enough context to find and fix it. A fallback is never an exception: the
    /// player still sees readable text. It is a report so that it is never silent either.
    /// </summary>
    public readonly struct FallbackReport : IEquatable<FallbackReport>
    {
        public FallbackKind Kind { get; }

        /// <summary>The locale that was active.</summary>
        public string Locale { get; }

        /// <summary>The pool table name, or null for the string table.</summary>
        public string Table { get; }

        /// <summary>The key, phrase, pool key or path involved.</summary>
        public string Key { get; }

        /// <summary>Optional human-readable detail.</summary>
        public string Detail { get; }

        public FallbackReport(FallbackKind kind, string locale, string table, string key, string detail = null)
        {
            Kind = kind;
            Locale = locale;
            Table = table;
            Key = key;
            Detail = detail;
        }

        /// <summary>Two reports are the same fallback when everything but the detail text matches.</summary>
        public bool Equals(FallbackReport other)
        {
            return Kind == other.Kind
                   && string.Equals(Locale, other.Locale, StringComparison.Ordinal)
                   && string.Equals(Table, other.Table, StringComparison.Ordinal)
                   && string.Equals(Key, other.Key, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is FallbackReport other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ (Locale != null ? StringComparer.Ordinal.GetHashCode(Locale) : 0);
                hash = (hash * 397) ^ (Table != null ? StringComparer.Ordinal.GetHashCode(Table) : 0);
                hash = (hash * 397) ^ (Key != null ? StringComparer.Ordinal.GetHashCode(Key) : 0);
                return hash;
            }
        }

        public override string ToString()
        {
            string where = Table == null ? Key : Table + "|" + Key;
            string text = Kind + " [" + Locale + "] " + where;
            return string.IsNullOrEmpty(Detail) ? text : text + " (" + Detail + ")";
        }
    }
}
