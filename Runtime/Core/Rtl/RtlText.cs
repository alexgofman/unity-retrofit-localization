using System;
using System.Collections.Generic;
using System.Text;

namespace RetrofitLocalization
{
    /// <summary>
    /// Prepares Arabic-script text for a renderer that only draws left to right, without breaking
    /// rich-text markup.
    ///
    /// A shaper reverses the text it is given. Hand it a string with markup and the markup is
    /// reversed as well: <c>&lt;color=red&gt;</c> comes out as <c>&gt;der=roloc&lt;</c> and is drawn as
    /// literal text. So this class splits a string into lines, tags and the plain runs between
    /// tags, shapes only the plain runs, and copies every tag through untouched.
    ///
    /// Shaping each run is not enough on its own. A right-to-left line also reads its runs from
    /// right to left, so the runs of a line are emitted in reverse order, and each opening tag
    /// trades places with its closing tag so the markup still nests correctly:
    /// <code>
    ///   logical:  AAA &lt;b&gt;BBB&lt;/b&gt; CCC        (typed order)
    ///   visual:   CCC &lt;b&gt;BBB&lt;/b&gt; AAA        (each run shaped, run order reversed)
    /// </code>
    ///
    /// Limits, by design: this is not the Unicode bidirectional algorithm. A left-to-right phrase
    /// that is split by a tag ("Super&lt;/b&gt; Game") is reordered run by run, and a tag that sets
    /// state without a closing form (<c>&lt;alpha&gt;</c>, <c>&lt;pos&gt;</c>) simply keeps its place between runs.
    /// Text reordered here also wraps from the wrong end if the renderer breaks the line, so keep
    /// such strings on one line or put the line breaks into the text.
    /// </summary>
    public static class RtlText
    {
        // Tags that stand alone: they have no closing form and therefore no partner to swap with.
        private static readonly HashSet<string> StandaloneTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "sprite", "quad", "space", "pos", "alpha", "page", "cr", "nbsp", "zwsp", "zwj", "shy"
        };

        private enum TokenKind
        {
            Text,
            Open,
            Close,
            Standalone,
            LineBreak
        }

        private sealed class Token
        {
            public readonly TokenKind Kind;
            public readonly string Text;
            public readonly string Name; // tag name in lower case; null for text

            public Token(TokenKind kind, string text, string name = null)
            {
                Kind = kind;
                Text = text;
                Name = name;
            }
        }

        /// <summary>
        /// True for an Arabic-script letter in its plain, unshaped form. Presentation forms (what a
        /// shaper produces), digits, punctuation and vowel marks do not count, which is what lets
        /// <see cref="Apply"/> recognise text that has already been shaped and leave it alone.
        /// </summary>
        public static bool IsArabicLetter(char c)
        {
            if (c < '\u0620' || c > '\u08C9') return false;
            return (c <= '\u064A' && c != '\u0640') // U+0640 is tatweel, a stretching stroke, not a letter
                   || (c >= '\u066E' && c <= '\u06D3' && c != '\u0670')
                   || c == '\u06D5'
                   || c == '\u06EE' || c == '\u06EF'
                   || (c >= '\u06FA' && c <= '\u06FC')
                   || c == '\u06FF'
                   || (c >= '\u0750' && c <= '\u077F')
                   || c >= '\u08A0';
        }

        /// <summary>True when <paramref name="text"/> contains at least one unshaped Arabic-script letter.</summary>
        public static bool ContainsArabicLetters(string text)
        {
            if (text == null) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (IsArabicLetter(text[i])) return true;
            }

            return false;
        }

        /// <summary>The mirrored counterpart of a bracket, or the character itself.</summary>
        public static char Mirror(char c)
        {
            switch (c)
            {
                case '(': return ')';
                case ')': return '(';
                case '[': return ']';
                case ']': return '[';
                case '{': return '}';
                case '}': return '{';
                case '<': return '>';
                case '>': return '<';
                case '\u00AB': return '\u00BB';
                case '\u00BB': return '\u00AB';
                case '\u2039': return '\u203A';
                case '\u203A': return '\u2039';
                default: return c;
            }
        }

        /// <summary>
        /// Returns <paramref name="text"/> ready to be drawn left to right. Text without unshaped
        /// Arabic letters comes back as the same instance, so calling this on every string of an
        /// Arabic build is cheap. Text that was already shaped is recognised by having presentation
        /// forms and no plain letters left, and is returned as it is.
        ///
        /// That guard only covers a whole string. Never embed shaped text into a larger string that
        /// is shaped again later: shape once, at the point where the text is assigned to a label.
        /// </summary>
        public static string Apply(string text, IRtlShaper shaper)
        {
            if (shaper == null) throw new ArgumentNullException(nameof(shaper));
            if (string.IsNullOrEmpty(text) || !ContainsArabicLetters(text)) return text;

            List<string> breaks;
            List<List<Token>> lines = Tokenize(text, out breaks);
            if (lines.Count == 1 && lines[0].Count == 1 && lines[0][0].Kind == TokenKind.Text)
            {
                return shaper.Shape(text); // one plain line: nothing to split
            }

            bool unbalanced;
            List<Token>[] carriedIn;
            List<Token>[] openAtEnd;
            TrackOpenTags(lines, out carriedIn, out openAtEnd, out unbalanced);

            var output = new StringBuilder(text.Length + 16);
            var buffer = new List<Token>();
            for (int i = 0; i < lines.Count; i++)
            {
                // A tag left open across a line break is closed at the end of the line and opened
                // again on the next one. Each line can then be reordered on its own.
                List<Token> line = unbalanced ? Balance(lines[i], carriedIn[i], openAtEnd[i], buffer) : lines[i];
                if (HasArabicText(line)) AppendReordered(output, line, shaper);
                else AppendVerbatim(output, line);
                if (i < breaks.Count) output.Append(breaks[i]);
            }

            return output.ToString();
        }

        private static List<List<Token>> Tokenize(string text, out List<string> breaks)
        {
            var lines = new List<List<Token>>();
            breaks = new List<string>();
            var current = new List<Token>();
            int textStart = 0;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\n')
                {
                    int end = i > textStart && text[i - 1] == '\r' ? i - 1 : i; // keep "\r\n" together
                    if (end > textStart) current.Add(new Token(TokenKind.Text, text.Substring(textStart, end - textStart)));
                    breaks.Add(text.Substring(end, i + 1 - end));
                    lines.Add(current);
                    current = new List<Token>();
                    i++;
                    textStart = i;
                    continue;
                }

                Token tag;
                int next;
                if (c == '<' && TryReadTag(text, i, out tag, out next))
                {
                    if (i > textStart) current.Add(new Token(TokenKind.Text, text.Substring(textStart, i - textStart)));
                    if (tag.Kind == TokenKind.LineBreak)
                    {
                        breaks.Add(tag.Text);
                        lines.Add(current);
                        current = new List<Token>();
                    }
                    else
                    {
                        current.Add(tag);
                    }

                    i = next;
                    textStart = i;
                    continue;
                }

                i++;
            }

            if (text.Length > textStart) current.Add(new Token(TokenKind.Text, text.Substring(textStart)));
            lines.Add(current);
            return lines;
        }

        // Reads "<name ...>", "</name>" or "<#RRGGBB>" at position start. Anything else that begins
        // with '<' ("a < b", "<3") is ordinary text.
        private static bool TryReadTag(string text, int start, out Token tag, out int next)
        {
            tag = null;
            next = start;

            int close = -1;
            for (int i = start + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '>')
                {
                    close = i;
                    break;
                }

                if (c == '<' || c == '\n') break;
            }

            if (close < 0) return false;

            int nameStart = start + 1;
            bool closing = text[nameStart] == '/';
            if (closing) nameStart++;
            if (nameStart >= close) return false;

            string name;
            if (text[nameStart] == '#')
            {
                name = "color"; // <#RRGGBB> is shorthand for <color=#RRGGBB> and is closed by </color>
            }
            else if (IsAsciiLetter(text[nameStart]))
            {
                int nameEnd = nameStart + 1;
                while (nameEnd < close && (IsAsciiLetter(text[nameEnd]) || text[nameEnd] == '-'
                                           || (text[nameEnd] >= '0' && text[nameEnd] <= '9')))
                {
                    nameEnd++;
                }

                name = text.Substring(nameStart, nameEnd - nameStart).ToLowerInvariant();
            }
            else
            {
                return false;
            }

            TokenKind kind;
            if (closing) kind = TokenKind.Close;
            else if (name == "br") kind = TokenKind.LineBreak;
            else if (text[close - 1] == '/' || StandaloneTags.Contains(name)) kind = TokenKind.Standalone;
            else kind = TokenKind.Open;

            tag = new Token(kind, text.Substring(start, close - start + 1), name);
            next = close + 1;
            return true;
        }

        // Records, for every line, which tags are already open when it starts and which are still
        // open when it ends. "unbalanced" is true when any line ends with a tag still open.
        private static void TrackOpenTags(List<List<Token>> lines, out List<Token>[] carriedIn,
            out List<Token>[] openAtEnd, out bool unbalanced)
        {
            carriedIn = new List<Token>[lines.Count];
            openAtEnd = new List<Token>[lines.Count];
            unbalanced = false;
            var open = new List<Token>();
            for (int i = 0; i < lines.Count; i++)
            {
                if (open.Count > 0) carriedIn[i] = new List<Token>(open);
                foreach (Token token in lines[i])
                {
                    if (token.Kind == TokenKind.Open)
                    {
                        open.Add(token);
                    }
                    else if (token.Kind == TokenKind.Close)
                    {
                        int match = LastIndexOfName(open, token.Name);
                        if (match >= 0) open.RemoveAt(match);
                    }
                }

                if (open.Count > 0)
                {
                    openAtEnd[i] = new List<Token>(open);
                    unbalanced = true;
                }
            }
        }

        private static List<Token> Balance(List<Token> line, List<Token> carriedIn, List<Token> openAtEnd,
            List<Token> buffer)
        {
            if (carriedIn == null && openAtEnd == null) return line;

            buffer.Clear();
            if (carriedIn != null) buffer.AddRange(carriedIn);
            buffer.AddRange(line);
            if (openAtEnd != null)
            {
                for (int i = openAtEnd.Count - 1; i >= 0; i--)
                {
                    buffer.Add(new Token(TokenKind.Close, "</" + openAtEnd[i].Name + ">", openAtEnd[i].Name));
                }
            }

            return buffer;
        }

        private static bool HasArabicText(List<Token> line)
        {
            foreach (Token token in line)
            {
                if (token.Kind == TokenKind.Text && ContainsArabicLetters(token.Text)) return true;
            }

            return false;
        }

        private static void AppendVerbatim(StringBuilder output, List<Token> line)
        {
            foreach (Token token in line) output.Append(token.Text);
        }

        // Emits the tokens of one right-to-left line from last to first. A tag that has a partner on
        // the line is emitted as its partner, which swaps every opening tag with its closing tag and
        // keeps the markup well nested in the reversed order.
        private static void AppendReordered(StringBuilder output, List<Token> line, IRtlShaper shaper)
        {
            int count = line.Count;
            var partner = new int[count];
            var open = new List<int>();
            for (int i = 0; i < count; i++)
            {
                partner[i] = -1;
                Token token = line[i];
                if (token.Kind == TokenKind.Open)
                {
                    open.Add(i);
                }
                else if (token.Kind == TokenKind.Close)
                {
                    for (int k = open.Count - 1; k >= 0; k--)
                    {
                        if (line[open[k]].Name != token.Name) continue;
                        partner[i] = open[k];
                        partner[open[k]] = i;
                        open.RemoveAt(k);
                        break;
                    }
                }
            }

            for (int i = count - 1; i >= 0; i--)
            {
                Token token = line[partner[i] >= 0 ? partner[i] : i];
                if (token.Kind != TokenKind.Text)
                {
                    output.Append(token.Text);
                }
                else if (ContainsArabicLetters(token.Text))
                {
                    output.Append(shaper.Shape(token.Text));
                }
                else if (HasLetterOrDigit(token.Text))
                {
                    output.Append(token.Text); // a number or a left-to-right word keeps its own order
                }
                else
                {
                    AppendMirrored(output, token.Text); // spaces and punctuation follow the line direction
                }
            }
        }

        private static void AppendMirrored(StringBuilder output, string text)
        {
            for (int i = text.Length - 1; i >= 0; i--)
            {
                char c = text[i];
                if (char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(text[i - 1]))
                {
                    output.Append(text[i - 1]).Append(c);
                    i--;
                    continue;
                }

                output.Append(Mirror(c));
            }
        }

        private static bool HasLetterOrDigit(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsLetterOrDigit(text[i])) return true;
            }

            return false;
        }

        private static int LastIndexOfName(List<Token> tokens, string name)
        {
            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                if (tokens[i].Name == name) return i;
            }

            return -1;
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }
    }
}
