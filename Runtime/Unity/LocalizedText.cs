using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RetrofitLocalization
{
    /// <summary>
    /// Keeps a label in sync with a string-table key. Works on a TextMeshPro text or on a legacy
    /// <see cref="Text"/>, whichever the GameObject has, because an older UI usually mixes both.
    ///
    /// For static labels. A label whose text depends on run-time values keeps being set from code,
    /// through <see cref="LocalizationService.Get(string,object[])"/>.
    /// </summary>
    [AddComponentMenu("Localization/Localized Text (Retrofit)")]
    [DisallowMultipleComponent]
    public sealed class LocalizedText : MonoBehaviour
    {
        [Tooltip("Key in the string table. It has to exist in the table of the source language.")]
        [SerializeField] private string _key;

        private TMP_Text _tmpText;
        private Text _legacyText;
        private bool _resolved;

        /// <summary>The key shown by this label. Assigning it updates the label at once.</summary>
        public string Key
        {
            get => _key;
            set
            {
                _key = value;
                Apply();
            }
        }

        private void OnEnable()
        {
            LocalizationService.OnLanguageChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            LocalizationService.OnLanguageChanged -= Apply;
        }

        /// <summary>Writes the text for the current key and language into the label.</summary>
        public void Apply()
        {
            if (!_resolved)
            {
                _tmpText = GetComponent<TMP_Text>();
                if (_tmpText == null) _legacyText = GetComponent<Text>();
                _resolved = true;
            }

            if (string.IsNullOrEmpty(_key)) return;

            string text = LocalizationService.Get(_key);
            if (_tmpText != null) _tmpText.text = text;
            else if (_legacyText != null) _legacyText.text = text;
        }
    }
}
