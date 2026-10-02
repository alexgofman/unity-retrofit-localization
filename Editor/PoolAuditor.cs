using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RetrofitLocalization.Editor
{
    /// <summary>
    /// Audits the string pools of every locale in the catalog.
    ///
    /// A translated pool with a missing key or the wrong number of entries falls back to the source
    /// language as a whole, and a blank entry falls back on its own. The game keeps working and the
    /// text stays readable, which is exactly why this kind of defect survives play testing. The
    /// audit lists each case per locale, together with slots that were added or dropped, and writes
    /// the full list to the report file named in the settings.
    ///
    /// Pools register when their owning type is first used. To cover types nothing has touched
    /// yet, the audit runs the static constructors of the types in the namespaces listed in the
    /// settings. A type that cannot initialise in Edit mode (it needs a loaded save, a scene) is
    /// listed as not covered; run the audit again in Play mode to include it.
    ///
    /// The active locale is not changed: the translated tables are read straight from the files.
    /// </summary>
    public static class PoolAuditor
    {
        private const string CoreAssembly = "RetrofitLocalization.Core";
        private const string UnityAssembly = "RetrofitLocalization.Unity";

        [MenuItem(RetrofitLocalizationMenu.Root + "Audit String Pools", priority = 21)]
        private static void AuditFromMenu()
        {
            Run(true);
        }

        /// <summary>
        /// Runs the audit and logs the summary: one console entry, a warning when anything is wrong.
        /// Returns the outcome so a build script can fail on <see cref="AuditOutcome.ProblemCount"/>.
        /// </summary>
        public static AuditOutcome Run(bool writeReport)
        {
            if (!Application.isPlaying)
            {
                AssetDatabase.Refresh();
                // Start from the settings as they are now, not from whatever an earlier Edit-mode
                // tool run loaded. Pools that are already registered stay registered.
                LocalizationService.Reset();
            }

            RetrofitLocalizationSettings settings = RetrofitLocalizationMenu.CurrentSettings;
            var header = new StringBuilder();

            bool ranConstructors = false;
            string[] namespaces = settings.PoolNamespaces;
            if (namespaces == null || namespaces.Length == 0)
            {
                header.AppendLine("No pool namespaces are set in the settings, so only pools that were already "
                                  + "registered are audited.");
            }
            else
            {
                var failures = new List<string>();
                int initialised = PoolOwnerTypes.Initialize(CandidateAssemblies(), namespaces, failures);
                ranConstructors = true;
                header.AppendLine(initialised + " pool-owning type(s) initialised.");
                if (failures.Count > 0)
                {
                    header.AppendLine(failures.Count + " type(s) could not initialise here, so their pools are NOT "
                                      + "covered. Run the audit again in Play mode once the state they need exists:");
                    foreach (string failure in failures) header.AppendLine("  " + failure);
                }
            }

            AuditIgnoreList ignore = LoadIgnoreList(settings, header);
            AuditOutcome outcome = LocalizationAudit.AuditPools(LocalizationService.Engine, ignore);

            string text = "[RetrofitLocalization] Pool audit: " + outcome.ProblemCount + " problem(s).\n"
                          + header + outcome.Summary;
            if (writeReport) text += WriteReport(settings, header + outcome.Summary + "\n" + outcome.Details);

            if (outcome.ProblemCount == 0) Debug.Log(text);
            else Debug.LogWarning(text);

            if (ranConstructors && !Application.isPlaying && DomainReloadOnPlayIsDisabled())
            {
                // A static constructor runs once per domain. One that ran here, or failed here,
                // would otherwise carry its Edit-mode outcome into the next Play session.
                Debug.Log("[RetrofitLocalization] Domain reload on entering Play mode is disabled, so the scripts are "
                          + "reloaded now to discard what the audit initialised.");
                EditorUtility.RequestScriptReload();
            }

            return outcome;
        }

        private static bool DomainReloadOnPlayIsDisabled()
        {
            return EditorSettings.enterPlayModeOptionsEnabled
                   && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;
        }

        // Only code that can call the service can own a pool, so only assemblies that reference
        // this package are scanned.
        private static List<Assembly> CandidateAssemblies()
        {
            var candidates = new List<Assembly>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    if (reference.Name == CoreAssembly || reference.Name == UnityAssembly)
                    {
                        candidates.Add(assembly);
                        break;
                    }
                }
            }

            return candidates;
        }

        private static AuditIgnoreList LoadIgnoreList(RetrofitLocalizationSettings settings, StringBuilder header)
        {
            if (string.IsNullOrEmpty(settings.AuditIgnoreFile)) return AuditIgnoreList.Empty;

            string path = ProjectPath(settings.AuditIgnoreFile);
            if (!File.Exists(path))
            {
                header.AppendLine("Ignore list not found: " + path);
                return AuditIgnoreList.Empty;
            }

            var ignore = new AuditIgnoreList(File.ReadAllLines(path));
            header.AppendLine("Ignore list: " + ignore.Count + " entr" + (ignore.Count == 1 ? "y" : "ies") + ".");
            return ignore;
        }

        private static string WriteReport(RetrofitLocalizationSettings settings, string report)
        {
            if (string.IsNullOrEmpty(settings.AuditReportPath)) return string.Empty;

            string path = ProjectPath(settings.AuditReportPath);
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(path, report, new UTF8Encoding(false));
                return "Full report: " + path;
            }
            catch (IOException e)
            {
                return "The report could not be written: " + e.Message;
            }
            catch (UnauthorizedAccessException e)
            {
                return "The report could not be written: " + e.Message;
            }
        }

        // Paths in the settings are relative to the project folder, the parent of Assets.
        private static string ProjectPath(string relativePath)
        {
            string projectFolder = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(projectFolder, relativePath));
        }
    }
}
