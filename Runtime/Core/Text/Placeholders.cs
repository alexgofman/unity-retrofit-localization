using System;
using System.Collections.Generic;

namespace RetrofitLocalization
{
    /// <summary>The difference between the <c>{n}</c> slots of a source string and its translation.</summary>
    public readonly struct PlaceholderParity
    {
        /// <summary>Slots the translation uses that the source never had.</summary>
        public IReadOnlyList<int> Extra { get; }

        /// <summary>Slots of the source that the translation no longer mentions.</summary>
        public IReadOnlyList<int> Dropped { get; }

        /// <summary>False when either string has braces that <c>string.Format</c> would reject.</summary>
        public bool WellFormed { get; }

        public bool IsMatch => WellFormed && Extra.Count == 0 && Dropped.Count == 0;

        public PlaceholderParity(IReadOnlyList<int> extra, IReadOnlyList<int> dropped, bool wellFormed)
        {
            Extra = extra;
            Dropped = dropped;
            WellFormed = wellFormed;
        }
    }

    /// <summary>
    /// Reads the <c>{0}</c>, <c>{1:N0}</c>, <c>{2,-8}</c> slots of a composite format string.
    ///
    /// A translation that adds a slot the source never had makes <c>string.Format</c> throw at run
    /// time, in that one language only. A translation that drops a slot silently loses content.
    /// Neither shows up until somebody opens that exact screen in that exact language, so both are
    /// worth checking mechanically.
    /// </summary>
    public static class Placeholders
    {
        private static readonly int[] NoSlots = Array.Empty<int>();

        /// <summary>
        /// Collects the distinct slot indices of <paramref name="format"/> in ascending order.
        /// Returns false when the braces are malformed (a lone brace, a non-numeric slot); the slots
        /// that could be read are still reported.
        /// </summary>
        public static bool TryGetSlots(string format, List<int> slots)
        {
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            slots.Clear();
            if (string.IsNullOrEmpty(format)) return true;

            bool wellFormed = true;
            int i = 0;
            while (i < format.Length)
            {
                char c = format[i];
                if (c == '}')
                {
                    // "}}" is an escaped brace; a lone "}" is an error for string.Format.
                    if (i + 1 < format.Length && format[i + 1] == '}') i += 2;
                    else { wellFormed = false; i++; }
                    continue;
                }

                if (c != '{')
                {
                    i++;
                    continue;
                }

                if (i + 1 < format.Length && format[i + 1] == '{')
                {
                    i += 2; // "{{" is an escaped brace
                    continue;
                }

                int close = ReadSlot(format, i, out int index);
                if (close < 0)
                {
                    wellFormed = false;
                    i++;
                    continue;
                }

                if (!slots.Contains(index)) slots.Add(index);
                i = close + 1;
            }

            slots.Sort();
            return wellFormed;
        }

        /// <summary>The distinct slot indices of <paramref name="format"/>, ascending.</summary>
        public static IReadOnlyList<int> Slots(string format)
        {
            if (string.IsNullOrEmpty(format) || format.IndexOf('{') < 0) return NoSlots;
            var slots = new List<int>();
            TryGetSlots(format, slots);
            return slots;
        }

        /// <summary>Compares the slots of a translation with the slots of its source string.</summary>
        public static PlaceholderParity Compare(string source, string translation)
        {
            var sourceSlots = new List<int>();
            var translationSlots = new List<int>();
            bool wellFormed = TryGetSlots(source, sourceSlots) & TryGetSlots(translation, translationSlots);

            List<int> extra = null;
            List<int> dropped = null;
            foreach (int slot in translationSlots)
            {
                if (!sourceSlots.Contains(slot)) (extra ?? (extra = new List<int>())).Add(slot);
            }

            foreach (int slot in sourceSlots)
            {
                if (!translationSlots.Contains(slot)) (dropped ?? (dropped = new List<int>())).Add(slot);
            }

            return new PlaceholderParity(
                extra ?? (IReadOnlyList<int>)NoSlots,
                dropped ?? (IReadOnlyList<int>)NoSlots,
                wellFormed);
        }

        /// <summary>
        /// <c>string.Format</c> that never throws: on a malformed format string, or one that asks for
        /// more arguments than were supplied, <paramref name="result"/> is the unformatted text and
        /// the return value is false so the caller can report it.
        /// </summary>
        public static bool TryFormat(IFormatProvider provider, string format, object[] args, out string result)
        {
            if (format == null)
            {
                result = null;
                return false;
            }

            if (args == null || args.Length == 0)
            {
                result = format;
                return true;
            }

            try
            {
                result = string.Format(provider, format, args);
                return true;
            }
            catch (FormatException)
            {
                result = format;
                return false;
            }
        }

        // Reads "{index[,alignment][:format]}" starting at the opening brace. Returns the position of
        // the closing brace, or -1 when this is not a valid slot.
        private static int ReadSlot(string format, int open, out int index)
        {
            index = 0;
            int i = open + 1;
            int digits = 0;
            while (i < format.Length && format[i] >= '0' && format[i] <= '9')
            {
                // string.Format caps the index far below this, so a longer run is simply malformed.
                if (digits >= 6) return -1;
                index = index * 10 + (format[i] - '0');
                digits++;
                i++;
            }

            if (digits == 0) return -1;

            while (i < format.Length && format[i] == ' ') i++;
            if (i >= format.Length) return -1;

            if (format[i] == '}') return i;
            if (format[i] != ',' && format[i] != ':') return -1;

            for (i++; i < format.Length; i++)
            {
                if (format[i] == '}') return i;
                if (format[i] == '{') return -1;
            }

            return -1;
        }
    }
}
