using System.Collections.Generic;

namespace RetrofitLocalization.Tests
{
    /// <summary>Table files held in memory, keyed by the path the localizer asks for.</summary>
    internal sealed class MemoryTextSource : ITextSource
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _loads = new Dictionary<string, int>();

        public MemoryTextSource With(string path, string json)
        {
            _files[path] = json;
            return this;
        }

        public string Load(string path)
        {
            _loads.TryGetValue(path, out int count);
            _loads[path] = count + 1;
            return _files.TryGetValue(path, out string json) ? json : null;
        }

        public int LoadCount(string path)
        {
            _loads.TryGetValue(path, out int count);
            return count;
        }
    }

    /// <summary>
    /// A stand-in shaper that makes the work of the segmenter visible: every run it is given comes
    /// back wrapped in square brackets, unchanged inside. Expected strings therefore show which
    /// runs were shaped and in what order they were emitted, without any real Arabic shaping.
    /// </summary>
    internal sealed class BracketShaper : IRtlShaper
    {
        public readonly List<string> Runs = new List<string>();

        public string Shape(string logicalRun)
        {
            Runs.Add(logicalRun);
            return "[" + logicalRun + "]";
        }
    }

    internal static class Fixtures
    {
        // Arabic words used as test data, written as escapes so the source reads the same in every editor.
        public const string Text = "\u0646\u0635";                         // "text"
        public const string Name = "\u0627\u0633\u0645";                   // "name"
        public const string Gold = "\u0630\u0647\u0628";                   // "gold"
        public const string Hello = "\u0645\u0631\u062D\u0628\u0627";      // "hello"
        public const string Peace = "\u0633\u0644\u0627\u0645";            // "peace", contains lam + alef
        public const string Book = "\u0643\u062A\u0627\u0628";             // "book"
        public const string Water = "\u0645\u0627\u0621";                  // "water", ends in hamza

        public static LocaleCatalog Catalog()
        {
            return new LocaleCatalog("en", new[]
            {
                new LocaleInfo("en", "English", "English", false, new[] { "English" }),
                new LocaleInfo("de", "Deutsch", "German", false, new[] { "German" }),
                new LocaleInfo("ru", "\u0420\u0443\u0441\u0441\u043A\u0438\u0439", "Russian", false, new[] { "Russian" }),
                new LocaleInfo("ko", "\uD55C\uAD6D\uC5B4", "Korean", false, new[] { "Korean" }),
                new LocaleInfo("ar", "\u0627\u0644\u0639\u0631\u0628\u064A\u0629", "Arabic", true, new[] { "Arabic" }),
                new LocaleInfo("fr", "Fran\u00E7ais", "French", false, new[] { "French" })
            });
        }

        /// <summary>
        /// A small set of tables. German deliberately lacks two keys, French has no table file at
        /// all, and the German "Tips" pool table has one pool of the wrong length.
        /// </summary>
        public static MemoryTextSource Files()
        {
            return new MemoryTextSource()
                .With("Localization/en", @"{
                    ""menu.play"": ""Play"",
                    ""menu.quit"": ""Quit"",
                    ""greeting"": ""Hello, {0}!"",
                    ""score.line"": ""{0} scored {1} points"",
                    ""item.picked"": ""{0} picked up"",
                    ""filed.under"": ""Filed under {0}"",
                    ""apples.one"": ""{0} apple"",
                    ""apples.other"": ""{0} apples"",
                    ""price.tag"": ""<color=#FFD54A>{0}</color> coins""
                }")
                .With("Localization/de", @"{
                    ""menu.play"": ""Spielen"",
                    ""greeting"": ""Hallo, {0}!"",
                    ""score.line"": ""{0} hat {1} Punkte erzielt"",
                    ""filed.under"": ""Unter {0} abgelegt, Platz {3}"",
                    ""apples.one"": ""{0} Apfel"",
                    ""apples.other"": ""{0} Äpfel""
                }")
                .With("Localization/ru", @"{
                    ""menu.play"": ""Играть"",
                    ""apples.one"": ""{0} яблоко"",
                    ""apples.few"": ""{0} яблока"",
                    ""apples.many"": ""{0} яблок"",
                    ""apples.other"": ""{0} яблока""
                }")
                .With("Localization/ko", @"{
                    ""item.picked"": ""{0}을(를) 주웠습니다"",
                    ""filed.under"": ""{0}(으)로 분류"",
                    ""apples.other"": ""사과 {0}개""
                }")
                .With("Localization/ar", "{ \"menu.play\": \"" + Text + "\", \"price.tag\": \"<color=#FFD54A>{0}</color> " + Gold + "\" }")
                .With("Localization/phrases_de", @"{
                    ""Settings"": ""Einstellungen"",
                    ""Continue"": ""Weiter"",
                    ""Shop"": ""Shop"",
                    ""Empty"": """"
                }")
                .With("Localization/phrases_ru", @"{
                    ""Settings"": ""Настройки"",
                    ""New Game"": ""Новая игра""
                }")
                .With("Localization/phrases_ar", "{ \"Settings\": \"" + Name + "\" }")
                .With("Localization/Pools/Tips_de", @"{
                    ""loading"": [""Tipp eins"", ""Tipp zwei"", ""Tipp drei""],
                    ""short"": [""nur einer""],
                    ""gaps"": [""Erster"", """", ""Dritter""]
                }")
                .With("Localization/Pools/Tips_ru", @"{
                    ""loading"": [""Совет один"", ""Совет два"", ""Совет три""]
                }");
        }

        public static Localizer Localizer(MemoryTextSource files = null)
        {
            return new Localizer(Catalog(), files ?? Files());
        }
    }
}
