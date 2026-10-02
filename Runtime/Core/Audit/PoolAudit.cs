using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RetrofitLocalization
{
    public enum PoolProblemKind
    {
        /// <summary>The pool table exists for the locale but has no entry for this pool.</summary>
        MissingPool,

        /// <summary>The translated pool has a different number of entries, so the runtime refuses it.</summary>
        LengthMismatch,

        /// <summary>One entry is blank and falls back on its own.</summary>
        EmptyElement,

        /// <summary>An entry uses a slot its source text does not have: formatting throws.</summary>
        ExtraPlaceholder,

        /// <summary>An entry no longer mentions a slot of its source text.</summary>
        DroppedPlaceholder,

        /// <summary>
        /// Every entry is identical to the source text. Not an error by itself (names, numbers), so
        /// it is listed but not counted as a problem.
        /// </summary>
        Untranslated
    }

    public readonly struct PoolProblem
    {
        public PoolProblemKind Kind { get; }
        public string Table { get; }
        public string Key { get; }

        /// <summary>Index of the entry, or -1 when the problem concerns the whole pool.</summary>
        public int Index { get; }

        public string Detail { get; }

        public PoolProblem(PoolProblemKind kind, string table, string key, int index = -1, string detail = null)
        {
            Kind = kind;
            Table = table;
            Key = key;
            Index = index;
            Detail = detail;
        }

        public override string ToString()
        {
            string id = AuditIgnoreList.Id(Table, Key);
            if (Index >= 0) id = AuditIgnoreList.Id(Table, Key, Index);
            return string.IsNullOrEmpty(Detail) ? Kind + " " + id : Kind + " " + id + " (" + Detail + ")";
        }
    }

    /// <summary>
    /// Pools and entries that were reviewed and are meant to stay as they are: a pool of proper
    /// names, an entry whose translation legitimately drops a slot. One id per line,
    /// <c>Table|Key</c> for a whole pool or <c>Table|Key[3]</c> for one entry; <c>#</c> starts a comment.
    /// </summary>
    public sealed class AuditIgnoreList
    {
        public static readonly AuditIgnoreList Empty = new AuditIgnoreList(null);

        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.Ordinal);

        public int Count => _ids.Count;

        public AuditIgnoreList(IEnumerable<string> lines)
        {
            if (lines == null) return;
            foreach (string raw in lines)
            {
                if (raw == null) continue;
                int comment = raw.IndexOf('#');
                string line = (comment >= 0 ? raw.Substring(0, comment) : raw).Trim();
                if (line.Length > 0 && line.IndexOf('|') > 0) _ids.Add(line);
            }
        }

        public bool ContainsPool(string table, string key)
        {
            return _ids.Contains(Id(table, key));
        }

        public bool ContainsEntry(string table, string key, int index)
        {
            return _ids.Contains(Id(table, key, index));
        }

        public static string Id(string table, string key)
        {
            return table + "|" + key;
        }

        public static string Id(string table, string key, int index)
        {
            return table + "|" + key + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
        }
    }

    /// <summary>What an audit found for one locale.</summary>
    public sealed class PoolAuditResult
    {
        private readonly Dictionary<string, int> _tablesWithoutFile = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<PoolProblem> _problems = new List<PoolProblem>();

        public string Locale { get; }

        /// <summary>Distinct pools that were checked.</summary>
        public int PoolCount { get; internal set; }

        /// <summary>Findings that were skipped because the ignore list covers them.</summary>
        public int IgnoredCount { get; internal set; }

        public IReadOnlyList<PoolProblem> Problems => _problems;

        /// <summary>
        /// Pool tables that have no file for this locale at all, with the number of pools each one
        /// leaves in the source language. Kept apart from the problems: a whole table without a
        /// translation is one decision (not translated yet, or not meant to be), not many defects.
        /// </summary>
        public IReadOnlyDictionary<string, int> TablesWithoutFile => _tablesWithoutFile;

        public PoolAuditResult(string locale)
        {
            Locale = locale;
        }

        /// <summary>Number of findings that need fixing: everything except <see cref="PoolProblemKind.Untranslated"/>.</summary>
        public int ProblemCount
        {
            get
            {
                int count = 0;
                foreach (PoolProblem problem in _problems)
                {
                    if (problem.Kind != PoolProblemKind.Untranslated) count++;
                }

                return count;
            }
        }

        public int Count(PoolProblemKind kind)
        {
            int count = 0;
            foreach (PoolProblem problem in _problems)
            {
                if (problem.Kind == kind) count++;
            }

            return count;
        }

        internal void Add(PoolProblem problem)
        {
            _problems.Add(problem);
        }

        internal void AddTableWithoutFile(string table)
        {
            _tablesWithoutFile.TryGetValue(table, out int pools);
            _tablesWithoutFile[table] = pools + 1;
        }
    }

    /// <summary>
    /// Compares translated string pools with their source text.
    ///
    /// At run time a pool with a missing key or a wrong length falls back to the source language as
    /// a whole, and a blank entry falls back on its own. The result is readable text in the wrong
    /// language, which is easy to miss when testing and impossible to miss for a player. The audit
    /// lists each of those cases, plus slot drift, per locale.
    /// </summary>
    public static class PoolAudit
    {
        /// <summary>Checks one pool of one locale and adds what it finds to <paramref name="result"/>.</summary>
        /// <param name="table">Pool table name.</param>
        /// <param name="key">Pool key.</param>
        /// <param name="source">The source-language entries.</param>
        /// <param name="localized">The entries as written in the translated table, or null when the key is absent.</param>
        /// <param name="tableHasFile">False when the locale has no file for this table at all.</param>
        /// <param name="ignore">Reviewed exceptions, or null.</param>
        /// <param name="result">Receives the findings.</param>
        public static void Check(string table, string key, string[] source, string[] localized, bool tableHasFile,
            AuditIgnoreList ignore, PoolAuditResult result)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (ignore == null) ignore = AuditIgnoreList.Empty;

            result.PoolCount++;

            if (!tableHasFile)
            {
                result.AddTableWithoutFile(table);
                return;
            }

            bool poolIgnored = ignore.ContainsPool(table, key);
            if (localized == null)
            {
                if (poolIgnored) result.IgnoredCount++;
                else result.Add(new PoolProblem(PoolProblemKind.MissingPool, table, key));
                return;
            }

            if (localized.Length != source.Length)
            {
                result.Add(new PoolProblem(PoolProblemKind.LengthMismatch, table, key, -1,
                    localized.Length.ToString(CultureInfo.InvariantCulture) + " entries, the source has "
                    + source.Length.ToString(CultureInfo.InvariantCulture)));
                return;
            }

            bool identical = source.Length > 0;
            for (int i = 0; i < source.Length; i++)
            {
                bool entryIgnored = poolIgnored || ignore.ContainsEntry(table, key, i);
                if (string.IsNullOrEmpty(localized[i]))
                {
                    identical = false;
                    if (entryIgnored) result.IgnoredCount++;
                    else result.Add(new PoolProblem(PoolProblemKind.EmptyElement, table, key, i));
                    continue;
                }

                if (!string.Equals(localized[i], source[i], StringComparison.Ordinal)) identical = false;

                PlaceholderParity parity = Placeholders.Compare(source[i], localized[i]);
                if (parity.Extra.Count > 0)
                {
                    result.Add(new PoolProblem(PoolProblemKind.ExtraPlaceholder, table, key, i,
                        FormatSlots(parity.Extra)));
                }

                if (parity.Dropped.Count > 0)
                {
                    // A language can make a slot unnecessary (a pronoun the verb already implies),
                    // so a reviewed omission can be listed as an exception.
                    if (entryIgnored) result.IgnoredCount++;
                    else result.Add(new PoolProblem(PoolProblemKind.DroppedPlaceholder, table, key, i,
                        FormatSlots(parity.Dropped)));
                }
            }

            if (identical && !poolIgnored) result.Add(new PoolProblem(PoolProblemKind.Untranslated, table, key));
        }

        private static string FormatSlots(IReadOnlyList<int> slots)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < slots.Count; i++)
            {
                if (i > 0) builder.Append(' ');
                builder.Append('{').Append(slots[i].ToString(CultureInfo.InvariantCulture)).Append('}');
            }

            return builder.ToString();
        }
    }
}
