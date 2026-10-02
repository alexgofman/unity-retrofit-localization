using System.Collections.Generic;
using NUnit.Framework;

namespace RetrofitLocalization.Tests
{
    public class TableJsonTests
    {
        private const string Backslash = "\\";

        [Test]
        public void ParseStrings_ReadsAFlatObject()
        {
            Dictionary<string, string> table = TableJson.ParseStrings("{ \"menu.play\": \"Play\", \"menu.quit\": \"Quit\" }");

            Assert.That(table.Count, Is.EqualTo(2));
            Assert.That(table["menu.play"], Is.EqualTo("Play"));
            Assert.That(table["menu.quit"], Is.EqualTo("Quit"));
        }

        [Test]
        public void ParseStrings_AcceptsAnEmptyObjectAndALeadingByteOrderMark()
        {
            Assert.That(TableJson.ParseStrings("  {  }  "), Is.Empty);
            Assert.That(TableJson.ParseStrings("\uFEFF{ \"a\": \"b\" }")["a"], Is.EqualTo("b"));
        }

        [Test]
        public void ParseStrings_DecodesEscapes()
        {
            // The JSON text spells the line break, the quotes, the accented letter, the backslash and
            // the tab as escape sequences; the parser has to turn each one into the character it stands for.
            string json = "{"
                          + "\"lines\": \"line one" + Backslash + "nline two\","
                          + "\"quote\": \"say " + Backslash + "\"hi" + Backslash + "\"\","
                          + "\"unicode\": \"caf" + Backslash + "u00E9\","
                          + "\"slash\": \"a" + Backslash + Backslash + "b\","
                          + "\"tab\": \"tab" + Backslash + "there\""
                          + "}";

            Dictionary<string, string> table = TableJson.ParseStrings(json);

            Assert.That(table["lines"], Is.EqualTo("line one\nline two"));
            Assert.That(table["quote"], Is.EqualTo("say \"hi\""));
            Assert.That(table["unicode"], Is.EqualTo("caf\u00E9"));
            Assert.That(table["slash"], Is.EqualTo("a\\b"));
            Assert.That(table["tab"], Is.EqualTo("tab\there"));
        }

        [Test]
        public void ParseStrings_KeepsNonAsciiTextAsWritten()
        {
            Dictionary<string, string> table = TableJson.ParseStrings("{ \"word\": \"" + Fixtures.Hello + "\" }");
            Assert.That(table["word"], Is.EqualTo(Fixtures.Hello));
        }

        [Test]
        public void ParseStrings_FirstOccurrenceOfADuplicateKeyWins_AndIsReported()
        {
            var duplicates = new List<string>();
            Dictionary<string, string> table =
                TableJson.ParseStrings("{ \"a\": \"first\", \"b\": \"x\", \"a\": \"second\" }", duplicates);

            Assert.That(table["a"], Is.EqualTo("first"));
            Assert.That(duplicates, Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void ParseStrings_TreatsNullAsNoEntry()
        {
            Dictionary<string, string> table = TableJson.ParseStrings("{ \"a\": null, \"b\": \"x\" }");
            Assert.That(table.ContainsKey("a"), Is.False);
            Assert.That(table["b"], Is.EqualTo("x"));
        }

        [Test]
        public void ParsePools_ReadsStringArrays()
        {
            Dictionary<string, string[]> pools =
                TableJson.ParsePools("{ \"tips\": [\"one\", \"two\"], \"empty\": [] }");

            Assert.That(pools["tips"], Is.EqualTo(new[] { "one", "two" }));
            Assert.That(pools["empty"], Is.Empty);
        }

        [Test]
        public void ParsePools_KeepsANullElementAsAnEmptySlot()
        {
            // The array keeps its length, so the blank is visible to an audit.
            Dictionary<string, string[]> pools = TableJson.ParsePools("{ \"tips\": [\"one\", null, \"three\"] }");

            Assert.That(pools["tips"].Length, Is.EqualTo(3));
            Assert.That(pools["tips"][1], Is.Null);
        }

        [TestCase("")]
        [TestCase("[\"not an object\"]")]
        [TestCase("{ \"a\": \"b\", }")]
        [TestCase("{ \"a\": 5 }")]
        [TestCase("{ \"a\": { \"nested\": \"x\" } }")]
        [TestCase("{ \"a\": \"unterminated }")]
        [TestCase("{ \"a\": \"b\" } trailing")]
        [TestCase("{ a: \"b\" }")]
        [TestCase("{ \"a\" \"b\" }")]
        public void ParseStrings_RejectsAnythingThatIsNotAFlatStringTable(string json)
        {
            Assert.Throws<TableFormatException>(() => TableJson.ParseStrings(json));
        }

        [Test]
        public void ParseStrings_RejectsUnknownEscapes()
        {
            Assert.Throws<TableFormatException>(() => TableJson.ParseStrings("{ \"a\": \"bad " + Backslash + "q\" }"));
            Assert.Throws<TableFormatException>(() => TableJson.ParseStrings("{ \"a\": \"bad " + Backslash + "u12G4\" }"));
        }

        [Test]
        public void ParsePools_RejectsAStringWhereAnArrayIsExpected()
        {
            Assert.Throws<TableFormatException>(() => TableJson.ParsePools("{ \"tips\": \"one\" }"));
            Assert.Throws<TableFormatException>(() => TableJson.ParsePools("{ \"tips\": [\"one\", 2] }"));
        }

        [Test]
        public void FormatErrors_CarryLineAndColumn()
        {
            var error = Assert.Throws<TableFormatException>(
                () => TableJson.ParseStrings("{\n  \"a\": \"b\",\n  \"c\": 7\n}"));

            Assert.That(error.Line, Is.EqualTo(3));
            Assert.That(error.Column, Is.EqualTo(8));
            Assert.That(error.Message, Does.Contain("line 3"));
        }
    }
}
