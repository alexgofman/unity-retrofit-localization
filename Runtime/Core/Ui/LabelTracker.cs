using System;
using System.Collections.Generic;

namespace RetrofitLocalization
{
    /// <summary>
    /// Remembers, for every text label a sweep has handled, what the label said before it was
    /// translated and what was written into it.
    ///
    /// Both halves matter. The written text makes repeated sweeps cheap: a label that still shows
    /// what was written last time is skipped. The original text makes a language change possible
    /// while the labels are on screen: a label that currently shows German cannot be looked up in a
    /// French table, but its remembered source text can. Switching back to the source language
    /// restores that text.
    ///
    /// The class knows nothing about any UI system; <typeparamref name="TLabel"/> is whatever
    /// handle the caller uses for a label.
    /// </summary>
    public sealed class LabelTracker<TLabel>
    {
        private struct State
        {
            public string Source;  // what the label said before translation
            public string Applied; // what the label was left showing
        }

        private readonly Dictionary<TLabel, State> _states;
        private List<TLabel> _dead;

        public LabelTracker(IEqualityComparer<TLabel> comparer = null)
        {
            _states = new Dictionary<TLabel, State>(comparer);
        }

        /// <summary>Number of labels being tracked.</summary>
        public int Count => _states.Count;

        /// <summary>
        /// True when the label is tracked and still shows exactly what was last written into it, in
        /// which case a sweep has nothing to do. This is the check that keeps repeated sweeps cheap.
        /// </summary>
        public bool IsUpToDate(TLabel label, string current)
        {
            return _states.TryGetValue(label, out State state)
                   && string.Equals(state.Applied, current, StringComparison.Ordinal);
        }

        /// <summary>
        /// Handles a label during a regular sweep. Returns the text to assign, or null to leave the
        /// label alone: either it has not changed since it was last handled, or its text has no
        /// translation.
        /// </summary>
        /// <param name="label">The label.</param>
        /// <param name="current">The text the label shows right now.</param>
        /// <param name="translate">Returns the translation of a source text, or null when there is none.</param>
        public string Sweep(TLabel label, string current, Func<string, string> translate)
        {
            if (translate == null) throw new ArgumentNullException(nameof(translate));
            if (string.IsNullOrEmpty(current)) return null;

            if (IsUpToDate(label, current)) return null; // unchanged since it was last handled

            // New label, or code assigned new text: whatever it shows now is source text.
            string translated = translate(current);
            _states[label] = new State { Source = current, Applied = translated ?? current };
            return translated;
        }

        /// <summary>
        /// Handles a label after the language changed. Returns the text to assign, or null when the
        /// label already shows the right thing. A label whose source text has no translation in the
        /// new language gets its source text back.
        /// </summary>
        public string Retranslate(TLabel label, string current, Func<string, string> translate)
        {
            if (translate == null) throw new ArgumentNullException(nameof(translate));

            // If the label still shows what was written, translate from the remembered source.
            // Otherwise code changed it in the meantime and the current text is the new source.
            string source = _states.TryGetValue(label, out State state)
                            && string.Equals(state.Applied, current, StringComparison.Ordinal)
                ? state.Source
                : current;

            if (string.IsNullOrEmpty(source))
            {
                _states.Remove(label);
                return null;
            }

            string target = translate(source) ?? source;
            _states[label] = new State { Source = source, Applied = target };
            return string.Equals(target, current, StringComparison.Ordinal) ? null : target;
        }

        /// <summary>The source text remembered for a label, if it is tracked.</summary>
        public bool TryGetSource(TLabel label, out string source)
        {
            if (_states.TryGetValue(label, out State state))
            {
                source = state.Source;
                return true;
            }

            source = null;
            return false;
        }

        /// <summary>Copies every tracked label into <paramref name="buffer"/> (cleared first).</summary>
        public void CopyLabelsTo(List<TLabel> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            buffer.Clear();
            buffer.AddRange(_states.Keys);
        }

        public bool Forget(TLabel label)
        {
            return _states.Remove(label);
        }

        /// <summary>
        /// Drops every label <paramref name="isDead"/> accepts, so labels that were destroyed do not
        /// accumulate for the lifetime of the application. Returns the number removed.
        /// </summary>
        public int Prune(Predicate<TLabel> isDead)
        {
            if (isDead == null) throw new ArgumentNullException(nameof(isDead));
            if (_states.Count == 0) return 0;

            if (_dead == null) _dead = new List<TLabel>();
            _dead.Clear();
            foreach (TLabel label in _states.Keys)
            {
                if (isDead(label)) _dead.Add(label);
            }

            for (int i = 0; i < _dead.Count; i++) _states.Remove(_dead[i]);
            int removed = _dead.Count;
            _dead.Clear();
            return removed;
        }

        public void Clear()
        {
            _states.Clear();
        }
    }
}
