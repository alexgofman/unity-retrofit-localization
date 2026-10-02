namespace RetrofitLocalization.Samples
{
    /// <summary>
    /// The shape of code this package was made for: prose kept in <c>static readonly</c> arrays,
    /// read from many places. The arrays are initialised once and never replaced, so a language
    /// change has to rewrite what is inside them.
    ///
    /// Retrofitting such a class takes two edits: add the one-line shim, and wrap each literal in
    /// it. Call sites do not change at all.
    /// </summary>
    internal static class DemoTips
    {
        // "DemoTips" is the pool table: Resources/<pool folder>/DemoTips_<locale>.json
        private static string[] L(string key, string[] source)
        {
            return LocalizationService.LocalizePool("DemoTips", key, source);
        }

        public static readonly string[] Tips = L("tips", new[]
        {
            "Translations live in plain JSON files, one per language.",
            "A missing translation falls back to the source language and is reported.",
            "This line comes from a static readonly array. It was rewritten in place when you switched.",
            "Numbers pick the plural form the language needs."
        });

        public static readonly string[] Greetings = L("greetings", new[]
        {
            "Good to see you, {0}.",
            "{0}, your workspace is ready."
        });
    }
}
