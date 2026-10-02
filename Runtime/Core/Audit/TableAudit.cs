using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RetrofitLocalization
{
    public enum TableProblemKind
    {
        /// <summary>The source table has the key, the translation does not: the source text is shown.</summary>
        MissingKey,

        /// <summary>The translation has a key the source table does not: probably a typo or a leftover.</summary>
        ExtraKey,

        /// <summary>The translation is blank although the source text is not.</summary>
        EmptyValue,

        /// <summary>A plural form the grammar of the locale needs is absent.</summary>
        MissingPluralForm,

        /// <summary>The translation uses a slot the source text does not have: formatting throws.</summary>
        ExtraPlaceholder,

        /// <summary>The translation no longer mentions a slot of the source text: content is lost.</summary>
        DroppedPlaceholder,

        /// <summary>The translation has braces a formatter rejects, while the source text is fine.</summary>
        MalformedPlaceholder,

        /// <summary>The translation contains a backslash followed by "n": a line break that was escaped twice.</summary>
        EscapedLineBreak
    }

    public readonly struct TableProblem
    {
        public TableProblemKind Kind { get; }
        public string Key { get; }
        public string Detail { get; }

        public TableProblem(TableProblemKind kind, string key, string detail = null)
        {
            Kind = kind;
            Key = key;
            Detail = detail;
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Detail) ? Kind + " " + Key : Kind + " " + Key + " (" + Detail + ")";
        }
    }

    /// <summary>
    /// Compares a translated table with the source-language table. Everything it finds is something
    /// the runtime would paper over with a fallback, which is exactly why it has to be checked
    /// before a build rather than noticed in one.
    /// </summary>
    public static class TableAudit
    {
        private const string LiteralLineBreak = "\\n";

        /// <summary>
        /// Checks key coverage, blank values and slot parity of a string table. Plural forms are
        /// compared by what each language needs, not key by key: a Russian table needs
        /// <c>.few</c> and <c>.many</c> forms the English table never has, and a Korean table needs
        /// no <c>.one</c> form at all.
        /// </summary>
        public static List<TableProblem> Compare(StringTable source, StringTable translation)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (translation == null) throw new ArgumentNullException(nameof(translation));

            var problems = new List<TableProblem>();
            HashSet<string> pluralKeys = FindPluralKeys(source);

            foreach (string key in Sorted(source.Keys))
            {
                if (IsFormOf(key, pluralKeys)) continue;

                source.TryGet(key, out string sourceText);
                if (!translation.TryGet(key, out string text))
                {
                    problems.Add(new TableProblem(TableProblemKind.MissingKey, key));
                    continue;
                }

                CheckText(key, sourceText, text, true, problems);
            }

            foreach (string pluralKey in Sorted(pluralKeys))
            {
                string otherForm = FormKey(pluralKey, PluralCategory.Other);
                source.TryGet(otherForm, out string sourceText);

                if (!translation.Contains(otherForm))
                    problems.Add(new TableProblem(TableProblemKind.MissingPluralForm, otherForm));

                foreach (PluralCategory category in PluralRules.IntegerCategories(translation.Locale))
                {
                    string form = FormKey(pluralKey, category);
                    if (category != PluralCategory.Other && !translation.Contains(form))
                        problems.Add(new TableProblem(TableProblemKind.MissingPluralForm, form));
                }

                for (int c = 0; c <= (int)PluralCategory.Other; c++)
                {
                    string form = FormKey(pluralKey, (PluralCategory)c);
                    // A form for exactly zero, one or two can spell the number out, so a dropped
                    // count slot is normal there. An added slot is an error in every form.
                    if (translation.TryGet(form, out string text)) CheckText(form, sourceText, text, false, problems);
                }
            }

            foreach (string key in Sorted(translation.Keys))
            {
                if (source.Contains(key) || IsFormOf(key, pluralKeys)) continue;
                problems.Add(new TableProblem(TableProblemKind.ExtraKey, key));
            }

            return problems;
        }

        /// <summary>
        /// Checks a phrase table, whose keys are the source texts themselves.
        /// </summary>
        /// <param name="phrases">Source text to translation.</param>
        /// <param name="expectedPhrases">
        /// Optional: every phrase any locale translates. A phrase missing here is reported, because a
        /// label that is translated in one language is meant to be translated in all of them.
        /// </param>
        public static List<TableProblem> CheckPhrases(IDictionary<string, string> phrases,
            IEnumerable<string> expectedPhrases = null)
        {
            if (phrases == null) throw new ArgumentNullException(nameof(phrases));

            var problems = new List<TableProblem>();
            if (expectedPhrases != null)
            {
                foreach (string phrase in Sorted(expectedPhrases))
                {
                    if (!phrases.ContainsKey(phrase))
                        problems.Add(new TableProblem(TableProblemKind.MissingKey, phrase));
                }
            }

            foreach (string phrase in Sorted(phrases.Keys))
            {
                CheckText(phrase, phrase, phrases[phrase], true, problems);
            }

            return problems;
        }

        private static void CheckText(string key, string sourceText, string text, bool reportDropped,
            List<TableProblem> problems)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (!string.IsNullOrEmpty(sourceText))
                    problems.Add(new TableProblem(TableProblemKind.EmptyValue, key));
                return;
            }

            var sourceSlots = new List<int>();
            // A source text with unbalanced braces is not a format string, so slots mean nothing.
            if (Placeholders.TryGetSlots(sourceText, sourceSlots))
            {
                PlaceholderParity parity = Placeholders.Compare(sourceText, text);
                if (!parity.WellFormed)
                    problems.Add(new TableProblem(TableProblemKind.MalformedPlaceholder, key));
                if (parity.Extra.Count > 0)
                    problems.Add(new TableProblem(TableProblemKind.ExtraPlaceholder, key, FormatSlots(parity.Extra)));
                if (reportDropped && parity.Dropped.Count > 0)
                    problems.Add(new TableProblem(TableProblemKind.DroppedPlaceholder, key, FormatSlots(parity.Dropped)));
            }

            if (text.IndexOf(LiteralLineBreak, StringComparison.Ordinal) >= 0
                && (sourceText == null || sourceText.IndexOf(LiteralLineBreak, StringComparison.Ordinal) < 0))
            {
                problems.Add(new TableProblem(TableProblemKind.EscapedLineBreak, key));
            }
        }

        // Keys that have plural forms in the source table: "items" when the table holds every form
        // the source language itself needs ("items.one" and "items.other" for English). Requiring
        // all of them keeps an ordinary key that merely ends in ".other" from being mistaken for one.
        private static HashSet<string> FindPluralKeys(StringTable source)
        {
            var pluralKeys = new HashSet<string>(StringComparer.Ordinal);
            string otherSuffix = "." + PluralRules.Suffix(PluralCategory.Other);
            foreach (string key in source.Keys)
            {
                if (key.Length <= otherSuffix.Length || !key.EndsWith(otherSuffix, StringComparison.Ordinal)) continue;

                string pluralKey = key.Substring(0, key.Length - otherSuffix.Length);
                bool complete = true;
                foreach (PluralCategory category in PluralRules.IntegerCategories(source.Locale))
                {
                    if (!source.Contains(FormKey(pluralKey, category)))
                    {
                        complete = false;
                        break;
                    }
                }

                if (complete) pluralKeys.Add(pluralKey);
            }

            return pluralKeys;
        }

        private static bool IsFormOf(string key, HashSet<string> pluralKeys)
        {
            int dot = key.LastIndexOf('.');
            if (dot <= 0) return false;
            return PluralRules.TryParseSuffix(key.Substring(dot + 1), out _)
                   && pluralKeys.Contains(key.Substring(0, dot));
        }

        private static string FormKey(string pluralKey, PluralCategory category)
        {
            return pluralKey + "." + PluralRules.Suffix(category);
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

        private static List<string> Sorted(IEnumerable<string> keys)
        {
            var sorted = new List<string>(keys);
            sorted.Sort(StringComparer.Ordinal);
            return sorted;
        }
    }
}
