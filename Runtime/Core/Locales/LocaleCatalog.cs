using System;
using System.Collections.Generic;

namespace RetrofitLocalization
{
    /// <summary>One language the project ships.</summary>
    public sealed class LocaleInfo
    {
        private static readonly string[] NoSystemLanguages = Array.Empty<string>();

        /// <summary>Locale code, which is also the table file name: <c>&lt;folder&gt;/&lt;Code&gt;.json</c>.</summary>
        public string Code { get; }

        /// <summary>Name shown in a language picker, written in the language itself.</summary>
        public string NativeName { get; }

        /// <summary>Name for logs and analytics.</summary>
        public string EnglishName { get; }

        /// <summary>True for right-to-left scripts, whose text needs shaping before display.</summary>
        public bool IsRtl { get; }

        /// <summary>
        /// Names of the operating-system languages that select this locale on first run. They are the
        /// names of the engine's system-language enum ("German", "ChineseSimplified"), kept as strings
        /// so this assembly needs no engine reference.
        /// </summary>
        public IReadOnlyList<string> SystemLanguages { get; }

        public LocaleInfo(string code, string nativeName, string englishName, bool isRtl = false,
            IEnumerable<string> systemLanguages = null)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("A locale needs a code.", nameof(code));
            Code = code;
            NativeName = string.IsNullOrEmpty(nativeName) ? code : nativeName;
            EnglishName = string.IsNullOrEmpty(englishName) ? code : englishName;
            IsRtl = isRtl;
            SystemLanguages = systemLanguages == null
                ? NoSystemLanguages
                : new List<string>(systemLanguages).ToArray();
        }

        public override string ToString()
        {
            return Code;
        }
    }

    /// <summary>
    /// The list of locales as data. Everything that needs "all languages" (the runtime, the validator,
    /// the pool audit, the locale switcher) reads this one list, so a newly added locale cannot be
    /// skipped by a tool that kept its own copy.
    /// </summary>
    public sealed class LocaleCatalog
    {
        private readonly List<LocaleInfo> _all = new List<LocaleInfo>();
        private readonly Dictionary<string, LocaleInfo> _byCode =
            new Dictionary<string, LocaleInfo>(StringComparer.Ordinal);
        private readonly string[] _targets;

        /// <summary>The language the code base was written in: the fallback of last resort.</summary>
        public string SourceLocale { get; }

        /// <summary>Every locale, in the order given (the order a picker shows them in).</summary>
        public IReadOnlyList<LocaleInfo> All => _all;

        /// <summary>
        /// Every locale except the source language, in ordinal order so reports stay comparable
        /// from run to run.
        /// </summary>
        public IReadOnlyList<string> TargetLocales => _targets;

        public LocaleCatalog(string sourceLocale, IEnumerable<LocaleInfo> locales)
        {
            if (string.IsNullOrEmpty(sourceLocale))
                throw new ArgumentException("A catalog needs a source locale.", nameof(sourceLocale));
            if (locales == null) throw new ArgumentNullException(nameof(locales));

            SourceLocale = sourceLocale;
            foreach (LocaleInfo locale in locales)
            {
                if (locale == null) continue;
                if (_byCode.ContainsKey(locale.Code))
                    throw new ArgumentException("Locale '" + locale.Code + "' is listed twice.", nameof(locales));
                _byCode.Add(locale.Code, locale);
                _all.Add(locale);
            }

            if (!_byCode.ContainsKey(sourceLocale))
                throw new ArgumentException(
                    "The source locale '" + sourceLocale + "' is not in the locale list.", nameof(locales));

            var targets = new List<string>();
            foreach (LocaleInfo locale in _all)
            {
                if (!string.Equals(locale.Code, sourceLocale, StringComparison.Ordinal)) targets.Add(locale.Code);
            }

            targets.Sort(StringComparer.Ordinal);
            _targets = targets.ToArray();
        }

        public bool Contains(string code)
        {
            return code != null && _byCode.ContainsKey(code);
        }

        public bool TryGet(string code, out LocaleInfo locale)
        {
            if (code == null)
            {
                locale = null;
                return false;
            }

            return _byCode.TryGetValue(code, out locale);
        }

        public bool IsRtl(string code)
        {
            return TryGet(code, out LocaleInfo locale) && locale.IsRtl;
        }

        /// <summary>
        /// The code of the first locale that lists <paramref name="systemLanguage"/>, or null.
        /// </summary>
        public string FindBySystemLanguage(string systemLanguage)
        {
            if (string.IsNullOrEmpty(systemLanguage)) return null;
            foreach (LocaleInfo locale in _all)
            {
                foreach (string name in locale.SystemLanguages)
                {
                    if (string.Equals(name, systemLanguage, StringComparison.OrdinalIgnoreCase)) return locale.Code;
                }
            }

            return null;
        }
    }
}
