using UnityEditor;
using UnityEngine;

namespace RetrofitLocalization.Editor
{
    /// <summary>Menu location and settings access shared by the Editor tools.</summary>
    internal static class RetrofitLocalizationMenu
    {
        public const string Root = "Tools/Retrofit Localization/";

        private const string SettingsFolder = "Assets/Resources";

        /// <summary>
        /// The settings every tool works with: the ones the running game uses in Play mode,
        /// otherwise the settings asset, otherwise the defaults.
        /// </summary>
        public static RetrofitLocalizationSettings CurrentSettings
        {
            get
            {
                return Application.isPlaying
                    ? LocalizationService.Settings
                    : RetrofitLocalizationSettings.LoadOrDefault();
            }
        }

        [MenuItem(Root + "Create Settings Asset", priority = 100)]
        private static void CreateSettingsAsset()
        {
            RetrofitLocalizationSettings settings =
                Resources.Load<RetrofitLocalizationSettings>(RetrofitLocalizationSettings.ResourceName);

            if (settings == null)
            {
                if (!AssetDatabase.IsValidFolder(SettingsFolder)) AssetDatabase.CreateFolder("Assets", "Resources");

                settings = ScriptableObject.CreateInstance<RetrofitLocalizationSettings>();
                AssetDatabase.CreateAsset(settings,
                    SettingsFolder + "/" + RetrofitLocalizationSettings.ResourceName + ".asset");
                AssetDatabase.SaveAssets();
            }

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
    }
}
