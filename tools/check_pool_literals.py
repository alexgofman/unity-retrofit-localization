#!/usr/bin/env python3
"""Lint: every string pool handed to the localization registry must be an inline literal.

LocalizePool keeps the array (or list) it is given and overwrites its contents on every language
change. An array that is also used for logic - compared against, used as a lookup key - would have
its words replaced by translations, and the logic would stop matching the first time a player
switches language. Nothing fails before that moment, so the mistake is worth catching statically.

A call passes when its last argument is created on the spot:

    new[] { ... }    new string[] { ... }    new List<string> { ... }    new() { ... }

or is an explicit copy:

    (string[])names.Clone()    names.ToArray()    names.ToList()    new List<string>(names)

A shim that forwards its own parameter, such as

    static string[] L(string key, string[] source) => LocalizationService.LocalizePool("Tips", key, source);

is recognised and accepted. Any other line that is correct for a reason this script cannot see can
be marked with a comment containing "pool-literal-ok".

Usage:
    python3 tools/check_pool_literals.py Assets/Scripts [more files or folders ...]
    python3 tools/check_pool_literals.py --call L --call Pool Assets/Scripts
    python3 tools/check_pool_literals.py --self-test

Exit status: 0 when clean, 1 when a call was flagged, 2 on a usage error.
"""

import argparse
import os
import re
import sys

DEFAULT_CALLS = ["L", "LocalizePool"]
MARKER = "pool-literal-ok"
SKIPPED_FOLDERS = {"bin", "obj", "Library", "Temp", ".git"}

# Words that can stand directly in front of a call. Anything else in that position (a type name,
# a closing bracket of an array or generic type) means the match is a method declaration.
KEYWORDS_BEFORE_CALL = {"return", "throw", "in", "case", "else", "await", "yield", "when", "and", "or", "not"}

INLINE = re.compile(
    r"^new\s*(\[\s*\]|string\s*\[\s*\]|(System\.Collections\.Generic\.)?List\s*<\s*string\s*>|\()"
)
COPY = re.compile(r"(\.Clone\(\)|\.ToArray\(\)|\.ToList\(\))$")


def mask(text):
    """Returns text with the contents of comments, strings and char literals blanked out.

    Length and line breaks are preserved, so positions in the masked text are positions in the
    original and brackets inside a string can no longer confuse the scanner.
    """
    out = list(text)
    i, n = 0, len(text)

    def blank(start, end):
        for k in range(start, min(end, n)):
            if out[k] != "\n":
                out[k] = " "

    while i < n:
        two = text[i:i + 2]
        if two == "//":
            end = text.find("\n", i)
            end = n if end < 0 else end
            blank(i, end)
            i = end
        elif two == "/*":
            end = text.find("*/", i + 2)
            end = n if end < 0 else end + 2
            blank(i, end)
            i = end
        elif text[i] == '"':
            verbatim = i > 0 and (text[i - 1] == "@" or text[i - 2:i] in ("@$", "$@"))
            j = i + 1
            while j < n:
                if verbatim:
                    if text[j] == '"':
                        if text[j + 1:j + 2] == '"':
                            j += 2
                            continue
                        break
                elif text[j] == "\\":
                    j += 2
                    continue
                elif text[j] == '"' or text[j] == "\n":
                    break
                j += 1
            blank(i + 1, j)
            i = j + 1
        elif text[i] == "'":
            j = i + 1
            while j < n and text[j] != "'" and text[j] != "\n":
                j += 2 if text[j] == "\\" else 1
            blank(i + 1, j)
            i = j + 1
        else:
            i += 1
    return "".join(out)


def matching(masked, start, opener="(", closer=")"):
    """Index of the bracket that closes the one at start, or -1."""
    depth = 0
    for i in range(start, len(masked)):
        c = masked[i]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
            if depth == 0:
                return i if c == closer else -1
    return -1


def split_arguments(masked, start, end):
    """Top-level comma positions between start and end, as (start, end) spans."""
    spans, depth, angle, begin = [], 0, 0, start
    for i in range(start, end):
        c = masked[i]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        elif c == "<":
            angle += 1
        elif c == ">" and angle > 0:
            angle -= 1
        elif c == "," and depth == 0 and angle == 0:
            spans.append((begin, i))
            begin = i + 1
    spans.append((begin, end))
    return spans


def is_declaration(masked, name_start):
    """True when the name at name_start is being declared rather than called."""
    before = masked[:name_start].rstrip()
    if not before:
        return False
    last = before[-1]
    if last in "]":
        return True
    if last == ">":
        return not before.endswith("=>")
    if last.isalnum() or last == "_":
        word = re.search(r"[A-Za-z_][A-Za-z0-9_]*$", before).group(0)
        return word not in KEYWORDS_BEFORE_CALL
    return False


def declaration_scope(masked, close_paren):
    """Span of the body that follows a declaration's parameter list."""
    i = close_paren + 1
    n = len(masked)
    while i < n and masked[i].isspace():
        i += 1
    if masked.startswith("=>", i):
        end = masked.find(";", i)
        return i, (n if end < 0 else end)
    if i < n and masked[i] == "{":
        end = matching(masked, i, "{", "}")
        return i, (n if end < 0 else end)
    return i, i


def check_text(text, calls=None, path="<text>"):
    """Returns a list of (path, line, message) for every call that is not given an inline literal."""
    calls = calls or DEFAULT_CALLS
    masked = mask(text)
    lines = text.split("\n")
    pattern = re.compile(r"(?<![A-Za-z0-9_])(%s)\s*\(" % "|".join(re.escape(c) for c in calls))

    forwarding = []  # (scope start, scope end, parameter names) of each declared shim
    found = []
    for match in pattern.finditer(masked):
        open_paren = match.end() - 1
        close_paren = matching(masked, open_paren)
        if close_paren < 0:
            continue

        spans = split_arguments(masked, open_paren + 1, close_paren)
        if is_declaration(masked, match.start(1)):
            names = [re.split(r"\s+", masked[a:b].split("=")[0].strip())[-1] for a, b in spans if masked[a:b].strip()]
            start, end = declaration_scope(masked, close_paren)
            forwarding.append((start, end, set(names)))
            continue

        if len(spans) < 2:
            continue  # not a pool call: it takes a key and a pool at the very least

        a, b = spans[-1]
        argument = re.sub(r"\s+", " ", text[a:b].strip())
        shape = re.sub(r"\s+", " ", masked[a:b].strip())
        if INLINE.match(shape) or COPY.search(shape):
            continue
        if any(s <= match.start() < e and shape in names for s, e, names in forwarding):
            continue  # a shim handing its own parameter on

        line = text.count("\n", 0, match.start()) + 1
        last_line = text.count("\n", 0, close_paren) + 1
        if any(MARKER in lines[k - 1] for k in range(line, last_line + 1)):
            continue

        shown = argument if len(argument) <= 60 else argument[:57] + "..."
        found.append((path, line, "%s(...) is given '%s': pass an inline literal or an explicit copy"
                      % (match.group(1), shown)))
    return found


def source_files(paths):
    for path in paths:
        if os.path.isfile(path):
            yield path
            continue
        for folder, subfolders, files in os.walk(path):
            subfolders[:] = sorted(d for d in subfolders if d not in SKIPPED_FOLDERS)
            for name in sorted(files):
                if name.endswith(".cs"):
                    yield os.path.join(folder, name)


def self_test():
    cases = [
        # (expected number of findings, source)
        (0, 'static readonly string[] A = L("a", new[] { "x", "y" });'),
        (0, 'static readonly string[] A = L("a", new string[] { "x" });'),
        (0, 'static readonly List<string> A = L("a", new List<string> { "x" });'),
        (0, 'static readonly List<string> A = L("a", new() { "x" });'),
        (0, 'static readonly string[] A = L("a",\n    new[]\n    {\n        "x, with a comma", "y (and a bracket"\n    });'),
        (0, 'static readonly string[] A = L("a", (string[])Names.Clone());'),
        (0, 'static readonly string[] A = L("a", Names.ToArray());'),
        (0, 'static readonly List<string> A = L("a", new List<string>(Names));'),
        (1, 'static readonly string[] A = L("a", Names);'),
        (1, 'static readonly string[] A = L("a", Lookup.Names);'),
        (1, 'var a = LocalizationService.LocalizePool("Tips", "a", shared);'),
        (0, 'var a = LocalizationService.LocalizePool("Tips", "a", shared); // pool-literal-ok: copied above'),
        (0, 'static string[] L(string key, string[] source) => LocalizationService.LocalizePool("T", key, source);'),
        (0, 'static string[] L(string key, string[] source)\n{\n    return LocalizationService.LocalizePool("T", key, source);\n}'),
        (1, 'static string[] L(string key, string[] source) => LocalizationService.LocalizePool("T", key, Other);'),
        (0, '// L("a", Names) in a comment\nstring s = "L(\\"a\\", Names)";'),
        (0, 'int Length(string s) { return s.Length; } var x = Label(1);'),
        (2, 'static readonly string[] A = L("a", Names);\nstatic readonly string[] B = L("b", new[] { "x" });\nstatic readonly string[] C = L("c", Other);'),
    ]
    failures = 0
    for expected, source in cases:
        actual = len(check_text(source))
        if actual != expected:
            failures += 1
            print("SELF-TEST FAILED: expected %d finding(s), got %d, for:\n%s\n" % (expected, actual, source))
    print("self-test: %d case(s), %d failure(s)" % (len(cases), failures))
    return 1 if failures else 0


def main(argv=None):
    parser = argparse.ArgumentParser(description="Checks that string pools are registered from inline literals.")
    parser.add_argument("paths", nargs="*", help="C# files or folders to scan")
    parser.add_argument("--call", action="append", dest="calls", metavar="NAME",
                        help="method name to check; repeatable (default: L and LocalizePool)")
    parser.add_argument("--self-test", action="store_true", help="run the built-in checks of this script")
    args = parser.parse_args(argv)

    if args.self_test:
        return self_test()
    if not args.paths:
        parser.print_usage(sys.stderr)
        return 2

    findings, scanned = [], 0
    for path in source_files(args.paths):
        scanned += 1
        with open(path, encoding="utf-8-sig") as handle:
            findings.extend(check_text(handle.read(), args.calls, path))

    for path, line, message in findings:
        print("%s:%d: %s" % (path, line, message))
    print("%d file(s) scanned, %d call(s) flagged" % (scanned, len(findings)))
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
