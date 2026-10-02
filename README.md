# Retrofit Localization for Unity

Localization for a Unity project that was written in one language, is already live, and cannot
be rebuilt around a localization framework.

## Why not Unity's Localization package?

If you are starting a project, use Unity's own Localization package. It has string and asset
tables, Smart Strings, locale fallbacks, pseudo-localization and import/export for translators,
and Unity maintains it. Nothing here competes with that for new work.

This package comes from the opposite situation: a game that had been live for years and was
English from the first line of code to the last prefab. In a code base like that,

- thousands of labels have their text baked into prefabs and scenes, in a mix of legacy `Text`
  and TextMeshPro;
- the prose lives in `static readonly string[]` fields spread over many classes, read from call
  sites all over the project and fed through `string.Format`;
- some of those very strings double as logic: they are compared, used as dictionary keys and
  written into save files.

Adopting a table-driven framework there means editing every prefab label, replacing every static
array and every call site, and then re-testing the whole game with save compatibility on the
line. The goal of a retrofit is the reverse: **add languages without changing call sites, prefabs
or save data, and leave the original-language build behaving exactly as it did** until a player
picks another language.

So this is not a framework. It is a small set of techniques for that one situation, packaged so
they can be tested. If your project does not have those constraints, you do not need it.

## What is in it

| Problem in an old code base | What the package does |
| --- | --- |
| No place to inject a service into existing call sites | A static facade, `LocalizationService.Get("key")`, over flat JSON tables |
| A missing translation shows the source language and nobody notices | Every fallback is raised as a `FallbackReport`, once per locale |
| Prose in `static readonly` arrays that are created once and cached everywhere | A registry that rewrites the **contents** of those arrays in place on a language change |
| Labels in prefabs that nobody is going to edit | A sweep that translates whatever source-language text a label shows, and puts it back |
| "1 items" | Plural categories for integer counts (the CLDR rules, including Russian, Polish and Arabic) |
| A Korean particle after an inserted name | Resolution of `을(를)`-style pairs from the final consonant of the preceding syllable |
| Arabic through a renderer that only draws left to right | Shaping that keeps rich-text tags intact, behind an `IRtlShaper` interface |
| Translated tables that drift from the source | Editor audits for keys, plural forms, `{0}` slots, pool lengths and blank entries |

All logic sits in `Runtime/Core`, which has no engine dependency and is covered by unit tests
that run with a plain .NET SDK. `Runtime/Unity` is a thin layer for Resources, PlayerPrefs and
UI components.

## The bug behind the audit

A translated pool that is missing, or that has a different number of entries than the original,
falls back to the source language as a whole. At run time that is the right call: pools are
often indexed in parallel, and an array of the wrong length would shift every line.

It is also a trap. During one content update a batch of translation files went out with a few
pools that did not line up with the code. Nothing crashed. Every screen showed readable text. It
was invisible in play testing. The update shipped with blocks of English in the middle of
translated screens, in several languages, and it was found by somebody reading through the
screens by hand, not by a test or a crash report.

A fallback that produces plausible output hides its own failures. That is why, in this package,
a fallback is always a report as well, and why the pool audit exists: it walks every registered
pool in every locale and lists exactly the cases the runtime would paper over.

## Install

Requires Unity 6000.0 or newer. TextMeshPro is part of `com.unity.ugui` 2.0, the only dependency.

In the Package Manager choose *Add package from git URL* and enter:

```
https://github.com/alexgofman/unity-retrofit-localization.git
```

Code in your own assembly definition has to reference `RetrofitLocalization.Unity` and
`RetrofitLocalization.Core`. Code without an assembly definition sees both automatically.

The *Language Picker Demo* sample (Package Manager, *Samples* tab) is the quickest way to see
everything below running: add its `LanguagePickerDemo` component to an empty GameObject in a
scratch scene and press Play.

## Setting up

1. **Settings.** *Tools > Retrofit Localization > Create Settings Asset* creates
   `Assets/Resources/RetrofitLocalizationSettings.asset`. List your locales there. The runtime,
   the validator, the pool audit and the locale switcher all read this one list.
2. **Tables.** One JSON file per locale in a Resources folder, `Localization/en.json`,
   `Localization/de.json`, and so on:

   ```json
   {
     "menu.play": "Play",
     "greeting.welcome": "Welcome back, {0}!",
     "files.count.one": "{0} file",
     "files.count.other": "{0} files"
   }
   ```

By default a player who has never picked a language stays on the source language, whatever the
device is set to. Nothing changes for existing players until you either offer a language picker
or switch on *Auto Detect System Locale* in the settings. The choice is kept in PlayerPrefs, not
in your save file.

One rule carries the whole design: **a translated string is only ever something to show.** It
must never become a save field, a lookup key, a product id or an analytics name. Those stay in
the source language.

## Using it

### Strings with a key

```csharp
title.text = LocalizationService.Get("menu.play");
hello.text = LocalizationService.Get("greeting.welcome", playerName);
count.text = LocalizationService.GetPlural("files.count", files);   // picks .one / .few / .many / .other
```

A missing translation never throws and never leaves a label blank. If the active locale lacks
the key you get the source-language text; if no table has it you get the key itself; if the text
cannot be formatted with the arguments you get it unformatted. In each case
`LocalizationService.OnFallback` is raised, once per distinct fallback while a locale is active,
and in the Editor and in development builds the fallback is logged.

For a static label, add the `LocalizedText` component and give it a key. It works on a legacy
`Text` or a TextMeshPro text and follows language changes by itself.

### Static string pools

This is the technique the package exists for. Before:

```csharp
static class Tips
{
    public static readonly string[] Loading = { "First tip", "Second tip" };
}
```

After, with one shim per class and no change at any call site:

```csharp
static class Tips
{
    static string[] L(string key, string[] source) => LocalizationService.LocalizePool("Tips", key, source);

    public static readonly string[] Loading = L("loading", new[] { "First tip", "Second tip" });
}
```

Translations go into `Localization/Pools/Tips_de.json`:

```json
{ "loading": ["Erster Tipp", "Zweiter Tipp"] }
```

`readonly` protects the reference, not the contents. The registry remembers the array together
with a private copy of its source text, and on a language change it overwrites the elements.
Every call site keeps the array it already holds. `List<string>` pools work the same way.

One precondition comes with this: **pass an inline literal** (or an explicit copy). The array
you pass is the one that gets overwritten, so an array that is also used for logic would stop
matching after a language change. `tools/check_pool_literals.py` checks this statically. Run it
from a clone of this repository, or copy the script into your project:

```
python3 tools/check_pool_literals.py path/to/your/Assets/Scripts
```

Keep pools in plain classes where you can. A pool owned by a `MonoBehaviour` or
`ScriptableObject` may be initialised while Unity is constructing the object, where table files
cannot be read; such a pool is registered at once but only receives its translation at the next
lookup made from ordinary code.

### Labels you are not going to edit

For text that was never given a key, add a phrase table next to the string tables. Its keys are
the source-language strings themselves:

```json
{ "Settings": "Einstellungen", "Sign out": "Abmelden" }
```

saved as `Localization/phrases_de.json`. While a locale with a phrase table is active,
`UITextLocalizer` looks at the labels on screen and swaps any text that matches a phrase exactly.
Text that does not match is left alone, text typed into an input field is never touched, and a
label whose text your code reads back as data can be excluded with `UITextLocalizer.Exempt`. A
TextMeshPro label that is filled through `SetText(format, ...)` or a char array is skipped as
well, because its `text` property does not report what it shows.

By default every active root canvas is swept on a timer, which needs no changes to your UI code.
If you would rather be explicit, set the sweep mode to *Registered Roots* and call
`UITextLocalizer.RegisterRoot(gameObject)` when a view appears. While the source language is
active the sweep does not run at all.

A label that is built by joining pieces matches no phrase as a whole. Translate the pieces
instead: `LocalizationService.Translate("Sign out")`.

### Switching language while the game runs

```csharp
LocalizationService.SetLocale("de");
```

In this order: the choice is saved, the tables are loaded, every registered pool is rewritten in
place, and then `OnLanguageChanged` is raised. What follows by itself:

- `LocalizedText` labels;
- swept labels, including a switch from one translation to another and back to the source
  language, because the sweep remembers what each label originally said;
- every string pool.

What does not: a label your code filled with `Get(...)`. Set it again from an
`OnLanguageChanged` handler, or reload the scene.

`LocalizationService.Reload()` reads the tables again and raises the same event, which is handy
after editing a table file while the game runs. `Configure` and `Reset` are for start-up and
raise nothing.

### Plurals

`GetPlural("files.count", n)` looks for `files.count.<category>` using the plural category of
`n` in the active language, then for `files.count.other`. Russian needs `.one`, `.few` and
`.many`; Arabic uses all six categories; Korean, Japanese and Chinese need only `.other`. When
the active locale has no entry at all, the source-language text is chosen with the source
language's own rule.

### Korean particles

A translator cannot know whether an inserted name ends in a consonant, so the convention is to
write both forms: `"{0}을(를) 열었습니다"`. After formatting, the pair is resolved from the final
consonant of the syllable in front of it: 검**을**, 사과**를**. The direction particle has its own
exception, 로 after a vowel *or* after ㄹ: 서울**로**, not 서울으로. This runs automatically in
`Get(key, args)` while a Korean locale is active; `KoreanParticles.Resolve` is the function
behind it.

### Arabic

Unity's UI text components draw left to right and do not join Arabic letters. For a locale
marked right-to-left, `Get` returns text that has been shaped: letters replaced by their joined
presentation forms and the line put into visual order.

Rich-text tags survive this. A shaper reverses what it is given, so `RtlText` splits a string
into lines, tags and the plain runs in between, shapes only the runs, emits the runs of a line
from right to left and swaps each opening tag with its closing tag so the markup still nests.

Two rules follow from "shaping reverses":

- Shape once. A value that goes into another sentence as an argument is fetched with `GetRaw`
  (or `TranslateRaw`), and the outer `Get` shapes the whole line.
- Pool entries hold text in typing order. Call `LocalizationService.ApplyRtl` where an entry is
  assigned to a label, after any `string.Format`.

Shaping goes through the `IRtlShaper` interface. The built-in `BasicArabicShaper` covers the
standard Arabic alphabet and is meant as a fallback that works out of the box. For production
Arabic, plug in a complete library. An adapter for
[RTLTMPro](https://github.com/pnarimani/RTLTMPro) by Mohamad Narimani (MIT licence) ships as the
*RTLTMPro Shaper* sample: install RTLTMPro yourself, import the sample and add
`RETROFIT_L10N_RTLTMPRO` to the scripting define symbols. RTLTMPro is not included in this
package.

The font has to contain the Arabic presentation forms (U+FE70 to U+FEFC).

## Editor tools

All under *Tools > Retrofit Localization*.

- **Locale Switcher.** One button per locale in the catalog. In Play mode it switches the
  running game; in Edit mode it sets the language the next Play session starts in.
- **Validate Tables.** Compares every string and phrase table with the source language: missing
  and extra keys, blank values, plural forms the language needs, `{0}` slots that were added or
  dropped, malformed braces.
- **Audit String Pools.** For every registered pool and every locale: pools missing from a
  table, length mismatches, blank entries, slot drift, pools still identical to the source text,
  and tables that have no file at all. Pools register when their class is first used, so list
  the namespaces of your pool-owning classes in the settings and the audit will initialise them
  first. A class that cannot initialise in Edit mode is listed as not covered; run the audit in
  Play mode for those. Reviewed exceptions go into an ignore file, one `Table|Key` or
  `Table|Key[index]` per line. The full report is written to the path given in the settings.

`TableValidator.Run()` and `PoolAuditor.Run(writeReport)` return an `AuditOutcome` with the
number of problems, so a build script can refuse to continue.

## Tests

```
dotnet test DotnetTests~
```

runs the engine-free tests with nothing but a .NET 8 SDK; this is what CI does. `Runtime/Core`
is built for .NET Standard 2.1 with C# 9 there, the same API surface and language version Unity
6 compiles it with.

The same test files, plus tests of the Unity layer, form the EditMode assembly in
`Tests/Editor`. To run them in the Test Runner, add the package to `testables` in your project
manifest.

## Status and limits

Extracted and refactored from production code. The production code shipped; this package, as
refactored, has not been run in a shipped build. Read it with that in mind.

What is verified:

- **Unit-tested** (a little over 300 NUnit tests, run by `dotnet test` in CI): plural rules,
  Korean particles, slot parsing and parity, the table reader, lookups and every kind of
  fallback report, the pool registry (source to translation and back, length-mismatch refusal,
  blank entries, list pools), the tag-aware segmenter, the built-in Arabic shaper, the label
  tracker behind the sweep, both audits, and the demo tables themselves.
- **Compiled** against the Unity 6000.0 managed assemblies with uGUI 2.0 and TextMeshPro, as
  C# 9 and outside the Editor: `Runtime/Unity`, `Editor`, the EditMode test assembly and the
  samples.

What is not:

- The EditMode tests in `Tests/Editor/Unity` and the demo script compile, but have not been run
  inside the Unity Editor for this release.
- Extracting the code changed more than names. These parts are new in the refactored version and
  have only been exercised by the unit tests: the engine-free core and its JSON table reader,
  the `key.one` / `key.other` convention for plural forms, fallback reports, live re-translation
  of swept labels, the reordering of runs in right-to-left lines, and the built-in Arabic shaper.
  In production, shaping was done by RTLTMPro.
- Two guards have never met the situation they are for. Skipping a TextMeshPro label that is
  driven through `SetText(format, ...)` only matters in a player build, where such a label
  behaves differently from the Editor. Deferring a pool that registers while Unity is
  constructing a component is something the tests can only imitate.

Known limits:

- **Right-to-left** means Arabic script; Hebrew is not handled. The reordering is not the
  Unicode bidirectional algorithm: a left-to-right phrase split by a tag is ordered run by run,
  and state-setting tags without a closing form keep their place. Shaped text wraps from the
  wrong end if the renderer breaks the line, so keep such strings on one line or put the line
  breaks into the text. The built-in shaper does not join the extra letters of Persian or Urdu.
- **Plurals** are for whole numbers. The "many" category that newer CLDR releases use for exact
  millions in French, Spanish, Italian and Portuguese is not modelled. A language the rules do
  not list uses `.other` for every count.
- **Korean particles** are resolved after Hangul only. After a digit or a Latin name the written
  pair is kept.
- **The sweep** matches whole strings exactly and runs on a timer, so a new label can show its
  source text for up to one interval (0.6 seconds by default) unless its root was registered.
  In *All Root Canvases* mode it searches for canvases on every tick.
- **Pools** have the inline-literal precondition above, and a run-time change to a list pool is
  undone by the next language change. Nothing here is thread-safe; use it from the main thread.
- **Tables** are read from Resources and parsed in full: the string and phrase table of a
  locale when it becomes active, a pool table when one of its pools is first needed. There is no
  Addressables support and no asset tables beyond the "`name_<locale>`" variant lookup of
  `LoadLocalizedAsset`.
- **Numbers** are formatted with the invariant culture unless you set a format provider.
- The demo translations are illustrative and have not been reviewed by native speakers.

## Credits

- [RTLTMPro](https://github.com/pnarimani/RTLTMPro) by Mohamad Narimani, MIT licence: the
  optional shaper integration. Not bundled.
- Plural categories follow the plural rules of the Unicode CLDR project; letter forms and
  joining behaviour follow the Unicode Standard.

## Licence

MIT. See [LICENSE](LICENSE).
