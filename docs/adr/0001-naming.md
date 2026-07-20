# ADR-1: Library naming — Parse, not Normalize

## Status

Accepted.

## Context

The library was originally named `DeltaZulu.Normalize`: a C# port of
`liblognorm` v2's PDAG-based engine, extracting typed fields from
unstructured log text via a compiled rulebase. "Normalize" was accurate for
what liblognorm itself calls this operation, but it collides with a second,
unrelated meaning this project is growing into: semantic normalization in
the ASIM/ECS sense (mapping extracted fields onto a common security-event
schema across sources). The two are different layers — one is grammar-driven
typed extraction from text, the other is schema-level semantic mapping — and
using "normalize" for both makes it impossible to talk about the roadmap
without ambiguity.

A third candidate, "decoder," was considered and rejected: it carries a
strong Wazuh connotation (Wazuh's rulebase-driven field-extraction stage is
literally called that), and adopting it here would misleadingly suggest
compatibility with, or descent from, that product's decoder format.

## Decision

Rename the library, package, assembly, and namespace to `DeltaZulu.Parse`.
"Parse"/"parser" now denotes exactly what this library does: turning
unstructured log text into typed fields via a grammar-driven rulebase. The
word "normalization" is reserved exclusively for the future semantic view
layer (ADR-5) that will sit on top of parsed output and map it onto a common
schema; nothing in this codebase uses "normalize" to describe what the
parser itself does.

Two naming choices fell out of this while renaming call sites:

- The core recursive PDAG-walking engine (formerly `Normalizer`) is named
  `PdagWalker`, not `Parser` — "parser" was already the established term for
  the ~30 individual motif parsers (`LiteralParser`, `StringParser`,
  `ParserTable`, ...), and reusing it for the walker would have created a
  real ambiguity between "a motif parser" and "the parser" in the same
  codebase.
- Identifiers that mirror liblognorm's own public API/vocabulary 1:1 for
  parity-tracking are renamed to match (`LogNormContext` → `ParseContext`,
  `NormalizeResult` → `ParseResult`, `LogNormOptions` → `ParseOptions`,
  `Normalize(...)` → `Parse(...)`), since they describe *our* library's
  identity. References to upstream C symbols and concepts by their own
  names — `ln_normalize`, `ln_normalizeRec`, the `lognormalizer` reference
  CLI/binary, the `LogNormalizer.Cli`/`lognormalizer` tool this port ships
  (itself named after that upstream binary) — are left untouched; they are
  not our naming to change. Unrelated, incidental uses of "normalize" in the
  generic software sense (JSON text normalization in `JsonText.cs`, Git's
  own line-ending normalization) are also untouched — they describe a
  different concept entirely and were never part of this ambiguity.

## Consequences

- Public API surface changes: `DeltaZulu.Normalize` → `DeltaZulu.Parse`,
  `LogNormContext` → `ParseContext`, `LogNormOptions` → `ParseOptions`,
  `NormalizeResult` → `ParseResult`, `Normalize(...)` → `Parse(...)`. Pre-1.0
  and pre-launch, so this ships as a clean break with no `[Obsolete]`
  forwarders.
- "Normalization" becomes available, unambiguously, for the semantic view
  layer described in ADR-5 — when that work starts, "normalize" will mean
  one thing in this codebase.
- The CLI tool (`LogNormalizer.Cli`, binary `lognormalizer`) keeps its name:
  it mirrors the upstream reference tool's own name, which this ADR does not
  govern.
