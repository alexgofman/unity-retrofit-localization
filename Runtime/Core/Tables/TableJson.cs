using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RetrofitLocalization
{
    /// <summary>Raised when a table file is not the JSON shape a table has to have.</summary>
    public sealed class TableFormatException : FormatException
    {
        public int Line { get; }
        public int Column { get; }

        public TableFormatException(string message, int line, int column)
            : base(message + " (line " + line.ToString(CultureInfo.InvariantCulture)
                   + ", column " + column.ToString(CultureInfo.InvariantCulture) + ")")
        {
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// A small strict reader for the two table shapes this package uses:
    /// <code>
    ///   { "menu.play": "Play", "menu.quit": "Quit" }                 a string table
    ///   { "tips": ["First tip", "Second tip"] }                      a pool table
    /// </code>
    /// It exists so the same parser serves the runtime, the Editor tools and the unit tests without
    /// a dependency on the engine's JSON module or a third-party library. It is not a general JSON
    /// reader: any other value type is an error, reported with its line and column.
    /// A <c>null</c> value means "no entry".
    /// </summary>
    public static class TableJson
    {
        /// <summary>Parses a flat key-to-string table.</summary>
        /// <param name="json">The file contents.</param>
        /// <param name="duplicateKeys">Optional: receives keys that occur more than once. The first occurrence wins.</param>
        public static Dictionary<string, string> ParseStrings(string json, ICollection<string> duplicateKeys = null)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var reader = new Reader(json);
            reader.ReadObject((key, r) =>
            {
                if (r.TryReadNull()) return;
                string value = r.ReadString();
                if (result.ContainsKey(key)) duplicateKeys?.Add(key);
                else result[key] = value;
            });
            return result;
        }

        /// <summary>Parses a key-to-string-array table.</summary>
        /// <param name="json">The file contents.</param>
        /// <param name="duplicateKeys">Optional: receives keys that occur more than once. The first occurrence wins.</param>
        public static Dictionary<string, string[]> ParsePools(string json, ICollection<string> duplicateKeys = null)
        {
            var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var reader = new Reader(json);
            reader.ReadObject((key, r) =>
            {
                if (r.TryReadNull()) return;
                string[] values = r.ReadStringArray();
                if (result.ContainsKey(key)) duplicateKeys?.Add(key);
                else result[key] = values;
            });
            return result;
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _position;

            public Reader(string text)
            {
                _text = text ?? throw new ArgumentNullException(nameof(text));
                // A UTF-8 byte order mark survives some loaders as a leading U+FEFF.
                _position = _text.Length > 0 && _text[0] == '\uFEFF' ? 1 : 0;
            }

            public void ReadObject(Action<string, Reader> readValue)
            {
                SkipWhitespace();
                Expect('{');
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _position++;
                }
                else
                {
                    while (true)
                    {
                        SkipWhitespace();
                        string key = ReadString();
                        SkipWhitespace();
                        Expect(':');
                        SkipWhitespace();
                        readValue(key, this);
                        SkipWhitespace();
                        if (Peek() == ',')
                        {
                            _position++;
                            continue;
                        }

                        Expect('}');
                        break;
                    }
                }

                SkipWhitespace();
                if (_position < _text.Length) throw Error("Unexpected text after the closing brace");
            }

            public string[] ReadStringArray()
            {
                Expect('[');
                var items = new List<string>();
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _position++;
                    return items.ToArray();
                }

                while (true)
                {
                    SkipWhitespace();
                    // A null element is kept as an empty slot so the array keeps its length.
                    items.Add(TryReadNull() ? null : ReadString());
                    SkipWhitespace();
                    if (Peek() == ',')
                    {
                        _position++;
                        continue;
                    }

                    Expect(']');
                    return items.ToArray();
                }
            }

            public bool TryReadNull()
            {
                if (string.CompareOrdinal(_text, _position, "null", 0, 4) != 0) return false;
                _position += 4;
                return true;
            }

            public string ReadString()
            {
                if (Peek() != '"') throw Error("Expected a string");
                _position++;

                // Fast path: no escape sequence, so the value is one substring.
                int start = _position;
                while (_position < _text.Length)
                {
                    char c = _text[_position];
                    if (c == '"')
                    {
                        string plain = _text.Substring(start, _position - start);
                        _position++;
                        return plain;
                    }

                    if (c == '\\') break;
                    if (c < ' ') throw Error("Unescaped control character in a string");
                    _position++;
                }

                var builder = new StringBuilder(_text.Length - start < 64 ? 64 : 256);
                builder.Append(_text, start, _position - start);
                while (_position < _text.Length)
                {
                    char c = _text[_position++];
                    if (c == '"') return builder.ToString();
                    if (c < ' ')
                    {
                        _position--;
                        throw Error("Unescaped control character in a string");
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (_position >= _text.Length) break;
                    char escape = _text[_position++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u': builder.Append(ReadUnicodeEscape()); break;
                        default:
                            _position--;
                            throw Error("Unknown escape sequence '\\" + escape + "'");
                    }
                }

                throw Error("Unterminated string");
            }

            private char ReadUnicodeEscape()
            {
                if (_position + 4 > _text.Length) throw Error("Incomplete \\u escape");
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    char c = _text[_position + i];
                    int digit;
                    if (c >= '0' && c <= '9') digit = c - '0';
                    else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                    else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                    else
                    {
                        _position += i;
                        throw Error("Invalid \\u escape");
                    }

                    value = (value << 4) | digit;
                }

                _position += 4;
                return (char)value;
            }

            private char Peek()
            {
                return _position < _text.Length ? _text[_position] : '\0';
            }

            private void Expect(char expected)
            {
                if (Peek() != expected) throw Error("Expected '" + expected + "'");
                _position++;
            }

            private void SkipWhitespace()
            {
                while (_position < _text.Length)
                {
                    char c = _text[_position];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r') break;
                    _position++;
                }
            }

            private TableFormatException Error(string message)
            {
                int line = 1;
                int column = 1;
                int end = _position < _text.Length ? _position : _text.Length;
                for (int i = 0; i < end; i++)
                {
                    if (_text[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }
                }

                return new TableFormatException(message, line, column);
            }
        }
    }
}
