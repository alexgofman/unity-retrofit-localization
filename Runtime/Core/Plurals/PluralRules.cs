using System;
using System.Collections.Generic;

namespace RetrofitLocalization
{
    /// <summary>CLDR plural categories for cardinal numbers.</summary>
    public enum PluralCategory
    {
        Zero,
        One,
        Two,
        Few,
        Many,
        Other
    }

    /// <summary>
    /// Cardinal plural rules for whole numbers, following the CLDR rule families.
    ///
    /// Only integers are modelled, which is what a "{0} items" label needs. Two CLDR details are
    /// left out on purpose: fractional operands, and the "many" category that newer CLDR releases
    /// add to French, Spanish, Italian, Portuguese and Catalan for exact multiples of a million.
    /// A language that is not listed here gets <see cref="PluralCategory.Other"/> for every count,
    /// which is the CLDR root rule.
    /// </summary>
    public static class PluralRules
    {
        private enum Family
        {
            OtherOnly,    // no grammatical plural after a number: ja, ko, zh, th, vi, id ...
            OneOther,     // "one" for exactly 1: en, de, es, it, tr ...
            ZeroOneIsOne, // "one" for 0 and 1: fr, pt (Brazilian)
            EastSlavic,   // ru, uk, be
            Polish,       // pl
            CzechSlovak,  // cs, sk
            Arabic        // ar: all six categories
        }

        private static readonly Dictionary<string, Family> Families =
            new Dictionary<string, Family>(StringComparer.OrdinalIgnoreCase)
            {
                { "ja", Family.OtherOnly }, { "ko", Family.OtherOnly }, { "zh", Family.OtherOnly },
                { "th", Family.OtherOnly }, { "vi", Family.OtherOnly }, { "id", Family.OtherOnly },
                { "ms", Family.OtherOnly }, { "km", Family.OtherOnly }, { "lo", Family.OtherOnly },
                { "my", Family.OtherOnly },

                { "en", Family.OneOther }, { "de", Family.OneOther }, { "nl", Family.OneOther },
                { "sv", Family.OneOther }, { "da", Family.OneOther }, { "nb", Family.OneOther },
                { "nn", Family.OneOther }, { "no", Family.OneOther }, { "fi", Family.OneOther },
                { "et", Family.OneOther }, { "el", Family.OneOther }, { "hu", Family.OneOther },
                { "tr", Family.OneOther }, { "bg", Family.OneOther }, { "es", Family.OneOther },
                { "it", Family.OneOther }, { "ca", Family.OneOther },
                // European Portuguese uses the n == 1 rule; plain "pt" (Brazilian) is listed below.
                { "pt-PT", Family.OneOther },

                { "fr", Family.ZeroOneIsOne }, { "pt", Family.ZeroOneIsOne },

                { "ru", Family.EastSlavic }, { "uk", Family.EastSlavic }, { "be", Family.EastSlavic },
                { "pl", Family.Polish },
                { "cs", Family.CzechSlovak }, { "sk", Family.CzechSlovak },
                { "ar", Family.Arabic }
            };

        private static readonly PluralCategory[] OtherOnlyForms = { PluralCategory.Other };
        private static readonly PluralCategory[] OneOtherForms = { PluralCategory.One, PluralCategory.Other };
        private static readonly PluralCategory[] SlavicForms =
            { PluralCategory.One, PluralCategory.Few, PluralCategory.Many };
        private static readonly PluralCategory[] CzechSlovakForms =
            { PluralCategory.One, PluralCategory.Few, PluralCategory.Other };
        private static readonly PluralCategory[] ArabicForms =
        {
            PluralCategory.Zero, PluralCategory.One, PluralCategory.Two,
            PluralCategory.Few, PluralCategory.Many, PluralCategory.Other
        };

        private static readonly string[] Suffixes = { "zero", "one", "two", "few", "many", "other" };

        private static readonly char[] TagSeparators = { '-', '_' };

        /// <summary>The plural category <paramref name="count"/> selects in <paramref name="locale"/>.</summary>
        public static PluralCategory GetCategory(string locale, long count)
        {
            // CLDR operands use the absolute value. Written this way so long.MinValue cannot overflow.
            ulong n = count < 0 ? (ulong)(-(count + 1)) + 1UL : (ulong)count;
            ulong mod10 = n % 10;
            ulong mod100 = n % 100;

            switch (FamilyOf(locale))
            {
                case Family.OneOther:
                    return n == 1 ? PluralCategory.One : PluralCategory.Other;

                case Family.ZeroOneIsOne:
                    return n <= 1 ? PluralCategory.One : PluralCategory.Other;

                case Family.EastSlavic:
                    // 1, 21, 31 ... but not 11.
                    if (mod10 == 1 && mod100 != 11) return PluralCategory.One;
                    // 2-4, 22-24 ... but not 12-14.
                    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return PluralCategory.Few;
                    // 0, 5-20, 25-30 ...
                    return PluralCategory.Many;

                case Family.Polish:
                    // Unlike Russian, only the number 1 itself is "one": 21 and 31 are "many".
                    if (n == 1) return PluralCategory.One;
                    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return PluralCategory.Few;
                    return PluralCategory.Many;

                case Family.CzechSlovak:
                    if (n == 1) return PluralCategory.One;
                    if (n >= 2 && n <= 4) return PluralCategory.Few;
                    return PluralCategory.Other;

                case Family.Arabic:
                    if (n == 0) return PluralCategory.Zero;
                    if (n == 1) return PluralCategory.One;
                    if (n == 2) return PluralCategory.Two;       // dual
                    if (mod100 >= 3 && mod100 <= 10) return PluralCategory.Few;   // plural noun
                    if (mod100 >= 11) return PluralCategory.Many;                 // singular accusative
                    return PluralCategory.Other;                                  // 100, 101, 102, 200 ...

                default:
                    return PluralCategory.Other;
            }
        }

        /// <summary>
        /// Every category <see cref="GetCategory"/> can return for <paramref name="locale"/>. A table
        /// needs one plural form per entry, plus the "other" form that is the fallback of last resort.
        /// </summary>
        public static IReadOnlyList<PluralCategory> IntegerCategories(string locale)
        {
            switch (FamilyOf(locale))
            {
                case Family.OneOther:
                case Family.ZeroOneIsOne:
                    return OneOtherForms;
                case Family.EastSlavic:
                case Family.Polish:
                    return SlavicForms;
                case Family.CzechSlovak:
                    return CzechSlovakForms;
                case Family.Arabic:
                    return ArabicForms;
                default:
                    return OtherOnlyForms;
            }
        }

        /// <summary>The lower-case CLDR name of a category, used as the key suffix of a plural form.</summary>
        public static string Suffix(PluralCategory category)
        {
            return Suffixes[(int)category];
        }

        /// <summary>Parses a key suffix ("one", "few" ...) back into its category.</summary>
        public static bool TryParseSuffix(string suffix, out PluralCategory category)
        {
            for (int i = 0; i < Suffixes.Length; i++)
            {
                if (string.Equals(Suffixes[i], suffix, StringComparison.Ordinal))
                {
                    category = (PluralCategory)i;
                    return true;
                }
            }

            category = PluralCategory.Other;
            return false;
        }

        private static Family FamilyOf(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return Family.OtherOnly;

            // A full tag wins ("pt-PT"), then the bare language ("zh-Hans" -> "zh").
            if (Families.TryGetValue(locale.Replace('_', '-'), out Family family)) return family;

            int separator = locale.IndexOfAny(TagSeparators);
            if (separator > 0 && Families.TryGetValue(locale.Substring(0, separator), out family)) return family;

            return Family.OtherOnly;
        }
    }
}
