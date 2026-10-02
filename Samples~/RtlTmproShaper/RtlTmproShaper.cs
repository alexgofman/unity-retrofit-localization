// Optional integration with RTLTMPro (https://github.com/pnarimani/RTLTMPro, MIT licence,
// copyright Mohamad Narimani). RTLTMPro is not part of this package and is not distributed
// with it: install it yourself, then add RETROFIT_L10N_RTLTMPRO to
// Project Settings > Player > Scripting Define Symbols to switch this file on.
//
// This script is a sample so that it compiles next to your own code, where RTLTMPro is visible
// however you installed it.
#if RETROFIT_L10N_RTLTMPRO
using RTLTMPro;
using UnityEngine;

namespace RetrofitLocalization.Samples
{
    /// <summary>
    /// Replaces the built-in <see cref="BasicArabicShaper"/> with RTLTMPro's shaper, which also
    /// handles the letters of Persian and other languages written in Arabic script.
    ///
    /// Only the string transform of RTLTMPro is used. Rich-text tags never reach it, because
    /// <see cref="RtlText"/> splits the text into tag-free runs first, so tag fixing is switched off.
    /// </summary>
    public sealed class RtlTmproShaper : IRtlShaper
    {
        public string Shape(string logicalRun)
        {
            if (string.IsNullOrEmpty(logicalRun)) return logicalRun;

            var output = new FastStringBuilder(logicalRun.Length + 16);
            // farsi: false, fixTextTags: false, preserveNumbers: true
            RTLSupport.FixRTL(logicalRun, output, false, false, true);
            return output.ToString();
        }

        // Runs after the package has cleared its statics for the play session and before any scene object wakes up.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            LocalizationService.SetRtlShaper(new RtlTmproShaper());
        }
    }
}
#endif
