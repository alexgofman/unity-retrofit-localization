using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RetrofitLocalization
{
    /// <summary>The result of an audit run: a count to gate on and two texts to read.</summary>
    public sealed class AuditOutcome
    {
        /// <summary>Number of findings that need fixing. Zero means the audit is clean.</summary>
        public int ProblemCount { get; }

        /// <summary>One line per locale, suitable for a console.</summary>
        public string Summary { get; }

        /// <summary>Every finding, grouped by locale, suitable for a report file.</summary>
        public string Details { get; }

        public AuditOutcome(int problemCount, string summary, string details)
        {
            ProblemCount = problemCount;
            Summary = summary;
            Details = details;
        }
    }

    /// <summary>
    /// Runs the table and pool checks over every locale of a catalog and formats the findings.
    /// It reads files through <see cref="ITextSource"/> only, so the same code runs from an Editor
    /// menu, from a unit test and from a build script.
    /// </summary>
    public static class LocalizationAudit
    {
        /// <summary>
        /// Checks the string table and the phrase table of every locale against the source language.
        /// </summary>
        public static AuditOutcome ValidateTables(LocaleCatalog catalog, LocalizerOptions options, ITextSource files)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (options == null) options = new LocalizerOptions();

            var summary = new StringBuilder();
            var details = new StringBuilder();
            int total = 0;

            StringTable source = LoadStringTable(catalog.SourceLocale, options, files, out string sourceError,
                out List<string> sourceDuplicates);
            if (source == null)
            {
                string line = "Source table " + options.TablePath(catalog.SourceLocale) + ": " + sourceError;
                return new AuditOutcome(1, line + Environment.NewLine, line + Environment.NewLine);
            }

            summary.AppendLine("String tables against '" + catalog.SourceLocale + "' ("
                               + Number(source.Count) + " keys):");
            if (sourceDuplicates.Count > 0)
            {
                total += sourceDuplicates.Count;
                AppendSection(details, catalog.SourceLocale, sourceDuplicates.Count);
                foreach (string key in sourceDuplicates) details.AppendLine("  DuplicateKey " + key);
            }

            foreach (string locale in catalog.TargetLocales)
            {
                var lines = new List<string>();
                StringTable table = LoadStringTable(locale, options, files, out string error, out List<string> duplicates);
                if (table == null)
                {
                    lines.Add("Table " + options.TablePath(locale) + ": " + error);
                }
                else
                {
                    foreach (string key in duplicates) lines.Add("DuplicateKey " + key);
                    foreach (TableProblem problem in TableAudit.Compare(source, table)) lines.Add(problem.ToString());
                }

                total += lines.Count;
                summary.AppendLine(lines.Count == 0
                    ? "  " + locale + ": OK"
                    : "  " + locale + ": " + Number(lines.Count) + " problem(s)");
                if (lines.Count == 0) continue;

                AppendSection(details, locale, lines.Count);
                foreach (string line in lines) details.AppendLine("  " + line);
            }

            total += ValidatePhrases(catalog, options, files, summary, details);
            return new AuditOutcome(total, summary.ToString(), details.ToString());
        }

        /// <summary>
        /// Checks every pool the localizer has registered against the pool tables of every locale.
        /// The active locale is neither used nor changed: the translated tables are read straight
        /// from the files. Pools register when their owning type is first used, so make sure those
        /// types have been touched (see <see cref="PoolOwnerTypes"/>) before calling this.
        /// </summary>
        public static AuditOutcome AuditPools(Localizer localizer, AuditIgnoreList ignore = null)
        {
            if (localizer == null) throw new ArgumentNullException(nameof(localizer));

            // The same table and key can be registered by several objects; audit each pair once.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var tables = new List<string>();
            var keys = new List<string>();
            var sources = new List<string[]>();
            localizer.Pools.ForEach((table, key, source) =>
            {
                if (!seen.Add(AuditIgnoreList.Id(table, key))) return;
                tables.Add(table);
                keys.Add(key);
                sources.Add(source);
            });

            var summary = new StringBuilder();
            var details = new StringBuilder();
            int total = 0;
            summary.AppendLine("String pools: " + Number(tables.Count) + " registered, "
                               + Number(localizer.Catalog.TargetLocales.Count) + " locale(s).");

            foreach (string locale in localizer.Catalog.TargetLocales)
            {
                var result = new PoolAuditResult(locale);
                var loaded = new Dictionary<string, PoolTable>(StringComparer.Ordinal);
                var malformed = new List<string>();

                for (int i = 0; i < tables.Count; i++)
                {
                    if (!loaded.TryGetValue(tables[i], out PoolTable poolTable))
                    {
                        try
                        {
                            poolTable = localizer.LoadPoolTable(tables[i], locale);
                        }
                        catch (TableFormatException e)
                        {
                            malformed.Add("MalformedTable " + tables[i] + " (" + e.Message + ")");
                        }

                        loaded[tables[i]] = poolTable;
                    }

                    string[] localized = null;
                    if (poolTable != null) poolTable.TryGet(keys[i], out localized);
                    PoolAudit.Check(tables[i], keys[i], sources[i], localized, poolTable != null, ignore, result);
                }

                int problems = result.ProblemCount + malformed.Count;
                total += problems;

                int poolsWithoutFile = 0;
                foreach (KeyValuePair<string, int> pair in result.TablesWithoutFile) poolsWithoutFile += pair.Value;

                summary.AppendLine("  " + locale + ": " + Number(problems) + " problem(s)"
                                   + " | missing " + Number(result.Count(PoolProblemKind.MissingPool))
                                   + " | length " + Number(result.Count(PoolProblemKind.LengthMismatch))
                                   + " | blank " + Number(result.Count(PoolProblemKind.EmptyElement))
                                   + " | slot added " + Number(result.Count(PoolProblemKind.ExtraPlaceholder))
                                   + " | slot dropped " + Number(result.Count(PoolProblemKind.DroppedPlaceholder))
                                   + " | untranslated " + Number(result.Count(PoolProblemKind.Untranslated))
                                   + " | ignored " + Number(result.IgnoredCount)
                                   + " | tables without a file " + Number(result.TablesWithoutFile.Count)
                                   + " (" + Number(poolsWithoutFile) + " pools)");

                AppendSection(details, locale, problems);
                foreach (string line in malformed) details.AppendLine("  " + line);

                var withoutFile = new List<string>(result.TablesWithoutFile.Keys);
                withoutFile.Sort(StringComparer.Ordinal);
                foreach (string table in withoutFile)
                {
                    details.AppendLine("  NoFile " + table + " (" + Number(result.TablesWithoutFile[table])
                                       + " pools stay in the source language)");
                }

                foreach (PoolProblem problem in result.Problems) details.AppendLine("  " + problem);
            }

            return new AuditOutcome(total, summary.ToString(), details.ToString());
        }

        // A phrase table is optional as a feature, but once one locale has it every locale should:
        // a label that is translated in one language is meant to be translated in all of them.
        private static int ValidatePhrases(LocaleCatalog catalog, LocalizerOptions options, ITextSource files,
            StringBuilder summary, StringBuilder details)
        {
            var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            var allPhrases = new HashSet<string>(StringComparer.Ordinal);

            foreach (string locale in catalog.TargetLocales)
            {
                string json = files.Load(options.PhraseTablePath(locale));
                if (json == null) continue;

                try
                {
                    Dictionary<string, string> phrases = TableJson.ParseStrings(json);
                    tables[locale] = phrases;
                    foreach (string phrase in phrases.Keys) allPhrases.Add(phrase);
                }
                catch (TableFormatException e)
                {
                    errors[locale] = e.Message;
                }
            }

            if (tables.Count == 0 && errors.Count == 0) return 0;

            int total = 0;
            summary.AppendLine("Phrase tables (" + Number(allPhrases.Count) + " phrases):");
            foreach (string locale in catalog.TargetLocales)
            {
                var lines = new List<string>();
                if (errors.TryGetValue(locale, out string error))
                {
                    lines.Add("Table " + options.PhraseTablePath(locale) + ": " + error);
                }
                else if (!tables.TryGetValue(locale, out Dictionary<string, string> phrases))
                {
                    lines.Add("Table " + options.PhraseTablePath(locale) + ": file not found");
                }
                else
                {
                    foreach (TableProblem problem in TableAudit.CheckPhrases(phrases, allPhrases))
                        lines.Add(problem.ToString());
                }

                total += lines.Count;
                summary.AppendLine(lines.Count == 0
                    ? "  " + locale + ": OK"
                    : "  " + locale + ": " + Number(lines.Count) + " problem(s)");
                if (lines.Count == 0) continue;

                AppendSection(details, locale + " phrases", lines.Count);
                foreach (string line in lines) details.AppendLine("  " + line);
            }

            return total;
        }

        private static StringTable LoadStringTable(string locale, LocalizerOptions options, ITextSource files,
            out string error, out List<string> duplicates)
        {
            duplicates = new List<string>();
            error = null;
            string json = files.Load(options.TablePath(locale));
            if (json == null)
            {
                error = "file not found";
                return null;
            }

            try
            {
                return new StringTable(locale, TableJson.ParseStrings(json, duplicates));
            }
            catch (TableFormatException e)
            {
                error = e.Message;
                return null;
            }
        }

        private static void AppendSection(StringBuilder details, string title, int problems)
        {
            details.AppendLine("=== " + title + " (" + Number(problems) + " problem(s)) ===");
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
