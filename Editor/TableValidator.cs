using UnityEditor;
using UnityEngine;

namespace RetrofitLocalization.Editor
{
    /// <summary>
    /// Checks the string and phrase tables of every locale in the catalog against the source
    /// language: missing and extra keys, blank values, missing plural forms and slot drift.
    ///
    /// At run time each of these is covered by a fallback and shows readable text, so none of them
    /// is visible unless somebody plays that screen in that language. The check itself is
    /// <see cref="LocalizationAudit.ValidateTables"/>; this class only gives it a menu entry and
    /// reads the tables the same way the game does.
    /// </summary>
    public static class TableValidator
    {
        [MenuItem(RetrofitLocalizationMenu.Root + "Validate Tables", priority = 20)]
        private static void ValidateFromMenu()
        {
            Run();
        }

        /// <summary>
        /// Validates the tables and logs the result: one console entry, a warning when anything is
        /// wrong. Returns the outcome so a build script can fail on <see cref="AuditOutcome.ProblemCount"/>.
        /// </summary>
        public static AuditOutcome Run()
        {
            // Pick up table files that were edited outside the Editor.
            if (!Application.isPlaying) AssetDatabase.Refresh();

            RetrofitLocalizationSettings settings = RetrofitLocalizationMenu.CurrentSettings;
            AuditOutcome outcome = LocalizationAudit.ValidateTables(
                settings.BuildCatalog(), settings.BuildOptions(), new ResourcesTextSource());

            string text = "[RetrofitLocalization] Table validation: " + outcome.ProblemCount + " problem(s).\n"
                          + outcome.Summary + outcome.Details;
            if (outcome.ProblemCount == 0) Debug.Log(text);
            else Debug.LogWarning(text);

            return outcome;
        }
    }
}
