# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-10-02

### Added

- `LocalizationService`: static facade over flat JSON string tables, with the player's choice
  kept in PlayerPrefs and optional detection of the device language.
- Fallback reporting: a lookup that falls back to the source language, to the key or to
  unformatted text raises a `FallbackReport` once per locale.
- `StringPoolRegistry`: in-place refresh of `static readonly string[]` and `List<string>` pools
  on a language change, with whole-pool refusal on a length mismatch.
- `UITextLocalizer`: label sweep driven by a phrase table, with `RegisterRoot`, exemptions,
  input-field protection and live re-translation when the language changes.
- `LocalizedText`: key-bound label component for legacy `Text` and TextMeshPro.
- `PluralRules`: integer plural categories, including the Russian, Polish, Czech and Arabic rules.
- `KoreanParticles`: two-form particle resolution from the final consonant, including the
  rieul exception of the direction particle.
- `RtlText`, `IRtlShaper` and `BasicArabicShaper`: tag-aware Arabic shaping with a built-in
  fallback shaper; optional RTLTMPro adapter as a sample.
- Editor tools: locale switcher, table validation and string-pool audit, all reading the locale
  catalog from the settings asset.
- `tools/check_pool_literals.py`: lint for pools that are not registered from an inline literal.
- Samples: language picker demo with tables in five languages.
- Engine-free unit tests that run with `dotnet test DotnetTests~`.
