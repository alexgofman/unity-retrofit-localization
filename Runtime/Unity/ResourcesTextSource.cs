using UnityEngine;

namespace RetrofitLocalization
{
    /// <summary>
    /// Reads table files as text assets from any Resources folder. The path is given without the
    /// file extension, as <see cref="Resources.Load(string)"/> expects: "Localization/de".
    /// </summary>
    public sealed class ResourcesTextSource : ITextSource
    {
        public string Load(string path)
        {
            TextAsset asset = Resources.Load<TextAsset>(path);
            return asset == null ? null : asset.text;
        }
    }
}
