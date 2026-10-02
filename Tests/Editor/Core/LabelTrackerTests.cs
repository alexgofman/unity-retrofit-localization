using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    /// <summary>
    /// The tracker is the logic behind sweeping UI labels. A label here is just a string id and its
    /// text lives in a dictionary, which is all the tracker needs to know about a UI.
    /// </summary>
    public class LabelTrackerTests
    {
        private static readonly Dictionary<string, string> German = new Dictionary<string, string>
        {
            { "Settings", "Einstellungen" }, { "Play", "Spielen" }
        };

        private static readonly Dictionary<string, string> French = new Dictionary<string, string>
        {
            { "Settings", "Param\u00E8tres" }, { "Shop", "Boutique" }
        };

        private LabelTracker<string> _tracker;
        private Dictionary<string, string> _screen;
        private int _lookups;

        [SetUp]
        public void SetUp()
        {
            _tracker = new LabelTracker<string>();
            _screen = new Dictionary<string, string>();
            _lookups = 0;
        }

        private Func<string, string> Using(Dictionary<string, string> table)
        {
            return source =>
            {
                _lookups++;
                return table != null && table.TryGetValue(source, out string translated) ? translated : null;
            };
        }

        // What a sweep does for one label: ask the tracker, assign the answer if there is one.
        private void Sweep(string label, Dictionary<string, string> table)
        {
            string text = _tracker.Sweep(label, _screen[label], Using(table));
            if (text != null) _screen[label] = text;
        }

        // What happens to every tracked label when the language changes.
        private void ChangeLanguage(Dictionary<string, string> table)
        {
            var labels = new List<string>();
            _tracker.CopyLabelsTo(labels);
            foreach (string label in labels)
            {
                string text = _tracker.Retranslate(label, _screen[label], Using(table));
                if (text != null) _screen[label] = text;
            }
        }

        [Test]
        public void Sweep_TranslatesALabel()
        {
            _screen["title"] = "Settings";

            Sweep("title", German);

            Assert.That(_screen["title"], Is.EqualTo("Einstellungen"));
        }

        [Test]
        public void Sweep_SkipsALabelThatHasNotChanged()
        {
            _screen["title"] = "Settings";
            Sweep("title", German);
            int lookupsAfterFirstSweep = _lookups;

            Sweep("title", German);
            Sweep("title", German);

            Assert.That(_lookups, Is.EqualTo(lookupsAfterFirstSweep));
            Assert.That(_screen["title"], Is.EqualTo("Einstellungen"));
        }

        [Test]
        public void Sweep_LeavesUnknownTextAlone_AndDoesNotLookItUpAgain()
        {
            _screen["name"] = "Tester";

            Sweep("name", German);
            Sweep("name", German);

            Assert.That(_screen["name"], Is.EqualTo("Tester"));
            Assert.That(_lookups, Is.EqualTo(1));
        }

        [Test]
        public void Sweep_TreatsTextAssignedByCodeAsNewSourceText()
        {
            _screen["button"] = "Settings";
            Sweep("button", German);

            _screen["button"] = "Play"; // the game reuses the label
            Sweep("button", German);

            Assert.That(_screen["button"], Is.EqualTo("Spielen"));
        }

        [Test]
        public void Sweep_IgnoresEmptyLabels()
        {
            _screen["blank"] = string.Empty;

            Sweep("blank", German);

            Assert.That(_tracker.Count, Is.EqualTo(0));
            Assert.That(_lookups, Is.EqualTo(0));
        }

        [Test]
        public void LanguageChange_TranslatesFromTheRememberedSourceText()
        {
            // The label shows German. A French table has no entry for "Einstellungen"; the switch
            // only works because the tracker still knows the label said "Settings".
            _screen["title"] = "Settings";
            Sweep("title", German);

            ChangeLanguage(French);

            Assert.That(_screen["title"], Is.EqualTo("Param\u00E8tres"));
        }

        [Test]
        public void LanguageChange_BackToTheSourceLanguage_RestoresTheOriginalText()
        {
            _screen["title"] = "Settings";
            Sweep("title", German);

            ChangeLanguage(null); // the source language has no phrase table

            Assert.That(_screen["title"], Is.EqualTo("Settings"));
        }

        [Test]
        public void LanguageChange_WithoutATranslationInTheNewLanguage_FallsBackToTheSourceText()
        {
            _screen["button"] = "Play";
            Sweep("button", German);
            Assert.That(_screen["button"], Is.EqualTo("Spielen"));

            ChangeLanguage(French); // French has no "Play"

            Assert.That(_screen["button"], Is.EqualTo("Play"));
        }

        [Test]
        public void LanguageChange_ReachesLabelsThatHadNoTranslationBefore()
        {
            _screen["shop"] = "Shop"; // not in the German table
            Sweep("shop", German);
            Assert.That(_screen["shop"], Is.EqualTo("Shop"));

            ChangeLanguage(French);

            Assert.That(_screen["shop"], Is.EqualTo("Boutique"));
        }

        [Test]
        public void LanguageChange_RespectsTextAssignedByCodeSinceTheLastSweep()
        {
            _screen["button"] = "Settings";
            Sweep("button", German);
            _screen["button"] = "Shop"; // set by code, not swept yet

            ChangeLanguage(French);

            Assert.That(_screen["button"], Is.EqualTo("Boutique"));
            Assert.That(_tracker.TryGetSource("button", out string source), Is.True);
            Assert.That(source, Is.EqualTo("Shop"));
        }

        [Test]
        public void RoundTrip_ThroughTwoLanguagesAndBack()
        {
            _screen["title"] = "Settings";
            Sweep("title", German);
            ChangeLanguage(French);
            ChangeLanguage(German);
            Assert.That(_screen["title"], Is.EqualTo("Einstellungen"));

            ChangeLanguage(null);
            Assert.That(_screen["title"], Is.EqualTo("Settings"));
        }

        [Test]
        public void Retranslate_ReturnsNull_WhenTheLabelAlreadyShowsTheRightText()
        {
            _screen["name"] = "Tester";
            Sweep("name", German);

            Assert.That(_tracker.Retranslate("name", "Tester", Using(French)), Is.Null);
        }

        [Test]
        public void IsUpToDate_IsTrueOnlyWhileTheLabelShowsWhatWasWritten()
        {
            _screen["title"] = "Settings";
            Assert.That(_tracker.IsUpToDate("title", "Settings"), Is.False); // not tracked yet

            Sweep("title", German);

            Assert.That(_tracker.IsUpToDate("title", "Einstellungen"), Is.True);
            Assert.That(_tracker.IsUpToDate("title", "Settings"), Is.False);
        }

        [Test]
        public void Prune_DropsLabelsThatNoLongerExist()
        {
            _screen["a"] = "Settings";
            _screen["b"] = "Play";
            _screen["c"] = "Tester";
            Sweep("a", German);
            Sweep("b", German);
            Sweep("c", German);

            int removed = _tracker.Prune(label => label != "b");

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(_tracker.Count, Is.EqualTo(1));
            Assert.That(_tracker.TryGetSource("b", out string source), Is.True);
            Assert.That(source, Is.EqualTo("Play"));
        }

        [Test]
        public void Forget_StopsTrackingOneLabel()
        {
            _screen["a"] = "Settings";
            Sweep("a", German);

            Assert.That(_tracker.Forget("a"), Is.True);
            Assert.That(_tracker.Count, Is.EqualTo(0));
        }
    }

    public class PhraseTableTests
    {
        private static PhraseTable Table()
        {
            return new PhraseTable("de", new Dictionary<string, string>
            {
                { "Settings", "Einstellungen" },
                { "New Game", "Neues Spiel" },
                { "Shop", "Shop" }, // identical: nothing to do
                { "Later", "" }     // blank: nothing to show
            });
        }

        [Test]
        public void TryTranslate_MatchesTheWholeText()
        {
            Assert.That(Table().TryTranslate("Settings", out string translated), Is.True);
            Assert.That(translated, Is.EqualTo("Einstellungen"));
        }

        [Test]
        public void TryTranslate_PutsSurroundingWhiteSpaceBack()
        {
            Assert.That(Table().TryTranslate("  New Game \n", out string translated), Is.True);
            Assert.That(translated, Is.EqualTo("  Neues Spiel \n"));
        }

        [Test]
        public void TryTranslate_DoesNotMatchPartOfALabel()
        {
            // A label built by joining pieces matches neither piece.
            Assert.That(Table().TryTranslate("Settings: New Game", out string translated), Is.False);
            Assert.That(translated, Is.Null);
        }

        [Test]
        public void BlankAndIdenticalEntries_AreDropped()
        {
            PhraseTable table = Table();

            Assert.That(table.Count, Is.EqualTo(2));
            Assert.That(table.TryTranslate("Shop", out _), Is.False);
            Assert.That(table.TryTranslate("Later", out _), Is.False);
        }

        [Test]
        public void MatchingIsCaseSensitive()
        {
            Assert.That(Table().TryTranslate("settings", out _), Is.False);
        }
    }

    public class UntranslatedPhraseLogTests
    {
        [TestCase("Daily Bonus", true)]
        [TestCase("OK", true)]
        [TestCase("Level 5", false)]                  // contains a digit: dynamic
        [TestCase("Saved.", false)]                   // a sentence
        [TestCase("Proceed with export?", false)]
        [TestCase("{0} left", false)]                 // a template
        [TestCase("<b>Bold</b>", false)]              // markup
        [TestCase("x", false)]                        // too short
        [TestCase("one two three four five six", false)] // too many words for a label
        public void LooksLikeLabel_KeepsShortPlainText(string text, bool expected)
        {
            Assert.That(UntranslatedPhraseLog.LooksLikeLabel(text), Is.EqualTo(expected));
        }

        [Test]
        public void Record_CollectsEachPhraseOnce_InOrdinalOrder()
        {
            var log = new UntranslatedPhraseLog();

            Assert.That(log.Record("Shop"), Is.True);
            Assert.That(log.Record(" Shop "), Is.False);
            Assert.That(log.Record("Level 5"), Is.False);
            Assert.That(log.Record("Bonus"), Is.True);
            Assert.That(log.IsDirty, Is.True);

            Assert.That(log.ToSortedList(), Is.EqualTo(new[] { "Bonus", "Shop" }));
            log.MarkSaved();
            Assert.That(log.IsDirty, Is.False);
        }
    }
}
