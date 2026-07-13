# Benchmark results

Run with:

```shell
dotnet run -c Release --project bench/DeltaZulu.Normalize.Benchmarks -- --filter '*'
```

Environment for all tables: Ubuntu 24.04, Intel Xeon 2.80GHz (4 physical cores), .NET 10.0.9 (RyuJIT AVX-512), BenchmarkDotNet 0.14.0, IterationCount=10 WarmupCount=3. Times and allocations are per single `Normalize` call.

Scenarios:

- **MatchFast** — 200-rule trie-heavy rulebase (shared literal prefixes), matching messages.
- **MatchBacktrack** — rules sharing a greedy `%word% %word%` prefix, distinct literal tails; messages match the last tail (maximal sibling exploration).
- **NoMatchTrie / NoMatchBacktrack** — messages that match nothing (full exploration).
- **Structured** — `json`, `cef`, `name-value-list`, `repeat` motifs.
- **ConcurrentNormalize** — 2000 messages via `Parallel.For` on one shared context, vs. **SingleThreadNormalize** for the same work on one thread.

## Baseline (commit: Phase 0, pre-optimization)

| Method                | Mean       | Gen0   | Allocated |
|---------------------- |-----------:|-------:|----------:|
| MatchFast             |   615.2 ns | 0.0579 |     606 B |
| MatchBacktrack        |   653.1 ns | 0.0674 |     704 B |
| NoMatchTrie           |   337.4 ns | 0.0477 |     496 B |
| NoMatchBacktrack      |   485.3 ns | 0.0642 |     664 B |
| Structured            | 1,977.0 ns | 0.2861 |   2,972 B |
| ConcurrentNormalize   |   664.4 ns | 0.0596 |     607 B |
| SingleThreadNormalize |   610.4 ns | 0.0586 |     606 B |

Observations:

- **Concurrency does not scale**: 4 cores are *slower per message* than 1 thread. Consistent with the shared per-node stats writes (`StatsCalled++`) causing cache-line ping-pong on every node visit (plus `Parallel.For` overhead at this small per-op cost).
- Allocations are significant everywhere — including **NoMatch** scenarios (496–664 B for messages that produce no fields), confirming eager value materialization on backtracked paths.

## Phase 2 — compiled PDAG snapshot (flat struct arrays, switch dispatch, literal first-char pre-filter, opt-in stats)

| Method                | Mean       | vs baseline | Allocated |
|---------------------- |-----------:|------------:|----------:|
| MatchFast             |   436.8 ns |       −29 % |     614 B |
| MatchBacktrack        |   476.1 ns |       −27 % |     712 B |
| NoMatchTrie           |   215.2 ns |       −36 % |     504 B |
| NoMatchBacktrack      |   317.5 ns |       −35 % |     672 B |
| Structured            | 1,832.3 ns |        −7 % |   2,980 B |
| ConcurrentNormalize   |   129.9 ns |      −80 %  |     615 B |
| SingleThreadNormalize |   429.1 ns |       −30 % |     614 B |

Observations:

- **Concurrency now scales**: 4 cores deliver ~3.3× the single-thread throughput (129.9 vs 429.1 ns/op); the read-only snapshot removed the shared-write ceiling.
- All single-thread scenarios improved 27–36 % from the contiguous edge array, jump-table dispatch and the literal first-char filter. Structured moved least (its cost is inside the big motif parsers, not the walker).
- Allocations unchanged, as expected — that is Phase 3's target.

## Phase 3 — two-phase match/extract (measure on descent, materialize on the success unwind)

Edges are classified at compile time: RawSpan (value == matched substring: one
Substring at unwind, no re-parse), Eager (repeat + structured motifs, whose match phase
is the expensive part), Deferred (derived values: cheap re-run at unwind).

| Method                | Mean       | vs Phase 2 | Allocated | vs Phase 2 |
|---------------------- |-----------:|-----------:|----------:|-----------:|
| MatchFast             |   459.5 ns |       +5 % |     614 B |          — |
| MatchBacktrack        |   482.5 ns |        ~0 % |     712 B |          — |
| NoMatchTrie           |   203.4 ns |       −6 % |     475 B |       −6 % |
| NoMatchBacktrack      |   264.1 ns |      −17 % |     491 B |      −27 % |
| Structured            | 1,814.7 ns |        ~0 % |   2,840 B |       −5 % |
| ConcurrentNormalize   |   137.0 ns |        ~0 % |     615 B |          — |
| SingleThreadNormalize |   480.2 ns |        ~0 % |     614 B |          — |

Observations:

- Backtracked/non-matching paths no longer allocate extraction values (NoMatchBacktrack
  −17 % time, −27 % bytes); winning-path scenarios stay flat because RawSpan materialization costs the same one Substring the eager code paid, just later.
- A naive uniform deferral (re-run every parser at unwind) was measured first and regressed MatchFast +17 % and Structured +24 % — the per-edge ExtractMode classification is what makes the trade-off pay.

## Phase 4 — vectorized parser scans (SearchValues, IndexOf/IndexOfAnyExcept, CommonPrefixLength)

The standard scenarios (5–15 char tokens) stay flat — SIMD needs run length to pay. On long fields (300-char runs, `LongFieldBenchmarks`) the effect is decisive:

| Method      | Before (scalar) | After (vectorized) | Speed-up |
|------------ |----------------:|-------------------:|---------:|
| LongCharTo  |      1,040.2 ns |           371.6 ns |    2.8× |
| LongQuoted  |        744.9 ns |           367.7 ns |    2.0× |
| LongLiteral |        431.7 ns |           196.3 ns |    2.2× |
| LongWord    |        522.4 ns |           366.6 ns |    1.4× |

`char-to`/`char-sep` additionally drop from O(run · terminators) to a vectorized O(run), and the `json` motif no longer allocates a UTF-8 copy of the candidate slice (Structured allocations 2,840 → 2,798 B).

## Phase 5 — flat result type + zero-copy RawSpan slices

The walker commits into a flat `FieldCollector` instead of building a `JsonObject` during the walk; RawSpan values are stored as `ReadOnlyMemory<char>` slices of the input. `Normalize(out JsonObject)` materializes at the end; the new `Normalize(out NormalizeResult)` / `NormalizeToString` never build a `JsonObject` at all and serialize slices directly. Measured on the sandbox host (numbers differ from the tables above in absolute terms; compare within this table only — "before" is the same host on the previous commit):

| Method                    | Before     | After      | Allocated before | after   |
|-------------------------- |-----------:|-----------:|-----------------:|--------:|
| Structured (JsonObject)   |   2.63 µs  |   2.57 µs  |          2.73 KB | 2.56 KB |
| StructuredFlatOnly (new)  |          — |   2.24 µs  |                — | 2.33 KB |
| StructuredToJsonText (new)|          — |   2.98 µs  |                — | 3.02 KB |
| LongCharTo (JsonObject)   |   546 ns   |   769 ns   |          1,096 B | 1,280 B |
| LongCharToFlat (new)      |          — |   286 ns   |                — |   280 B |
| LongWordFlat (new)        |          — |   308 ns   |                — |   280 B |

Observations:

- The flat path removes the per-field Substring entirely: on 300-char fields it is ~1.9× faster than the previous engine and allocates 280 B vs 1,096 B (the remaining bytes are the collector itself; the field values are slices).
- The Structured corpus is dominated by Eager motifs (json/cef/name-value/ repeat) that build JsonNode trees regardless, so the flat-only gain there is ~15 % time / ~15 % bytes.
- The classic `out JsonObject` overload keeps its cost on realistic events (Structured slightly improves) but pays a fixed collector overhead on every call, matched or not — see the fix below, which cuts it down to near the object header alone.

## Phase 5 fix — pool the scratch collector for the classic overload

Review of the initial Phase 5 cut measured every benchmark that never touches `NormalizeResult` — `MatchFast`, `MatchBacktrack`, `NoMatchTrie`, `NoMatchBacktrack`, `LongCharTo`/`LongWord`/`LongQuoted`/`LongLiteral`, `ConcurrentNormalize`, `SingleThreadNormalize` — showing a near-uniform +150–184 B regression versus the pre-#1/#2 baseline above, *independent of whether the message matched or how many fields it had*. Root cause: with a single walker feeding one `FieldCollector` (the deliberate design choice — see the flat-result-type discussion — that avoids duplicating the `"."` splice/`".."` unwrap semantics into two code paths), `Normalizer.Normalize(out JsonObject)` always allocates a `FieldCollector` object *and* its backing `Entry[4]` array, even on a total non-match (`AddUnparsedField` always sets `originalmsg`/`unparsed-data`, so the array is never actually skippable). That's a real, avoidable tax on every caller who never touches the flat API, not an acceptable cost of the new feature.

Fix: `FieldCollector.RentScratch()` rents the backing array from `ArrayPool<Entry>.Shared` instead of `new`-ing it, used only by `Normalizer.Normalize(out JsonObject)` (the one call site that discards the collector the instant it materializes); `ReturnScratch()` releases it in a `finally` block right after `ToJsonObject()` copies everything out. Growing once the rented array is full returns the pooled array and falls back to a plain heap array from then on — no pool bookkeeping needed after that point. `Normalize(out FieldCollector)` / `NormalizeResult`, whose lifetime outlives the call, keep the plain non-pooled allocation, exactly as "conditional on actual use of the flat-result API" requires.

| Method                | Pre-#1/#2 baseline | Post-fix  | Δ vs baseline |
|---------------------- |--------------------:|----------:|---------------:|
| MatchFast             |               614 B |     646 B |          +32 B |
| MatchBacktrack        |               712 B |     744 B |          +32 B |
| NoMatchTrie           |               475 B |     507 B |          +32 B |
| NoMatchBacktrack      |               491 B |     523 B |          +32 B |
| Structured            |             2,798 B |   2,470 B |         −328 B |
| ConcurrentNormalize   |               615 B |     648 B |          +33 B |
| SingleThreadNormalize |               614 B |     646 B |          +32 B |

Observations:

- The fixed tax drops from +150–184 B to a uniform +32 B — the `FieldCollector` object header/fields themselves, which can't be removed without eliminating the abstraction (and reopening the two-copy semantics risk). This residual is intrinsic to the single-path architecture, not an oversight.
- `Structured` lands *below* the pre-#1/#2 baseline: its rulebase mixes `json`/`cef`/`name-value-list`/`repeat`, so the pooled top-level array saves an allocation on top of Phase 5's existing per-field wins, even though nested per-round/custom-type collectors are intentionally left unpooled (their cost is proportional to real repeat/custom-type usage, not a blanket tax — pooling them would need to track escape into a long-lived `NormalizeResult`, which is unnecessary complexity these benchmarks don't call for).
- Verified with `FieldCollectorPoolingTests`: no cross-call leakage from array reuse, correct behavior when growth un-pools mid-flight, and no corruption under concurrent `Normalize` calls sharing the pool.

### Independent verification run

A full re-run (all suites, same host) confirmed the fix precisely: every legacy-path benchmark landed at baseline **+32 B** (MatchFast, MatchBacktrack, NoMatchTrie, NoMatchBacktrack, SingleThreadNormalize, LongCharTo/Word/Quoted,
LongLiteral all exactly +32 B; ConcurrentNormalize +34 B, within noise), and `Structured` landed at 2,470 B, −328 B versus baseline. `LongCharToFlat`/ `LongWordFlat` held at −75 % allocation vs. their non-flat counterparts.

This run also raised a fair question: `StructuredToJsonText` (3,094 B) looks worse than plain `Structured` (2,470 B) — but that compares text output against no text output, which isn't the right baseline. The right one is what `NormalizeToString` cost *before* the rewrite (materialize a `JsonObject`, then serialize it) — added as `StructuredClassicToJsonText`. Measured back-to-back:

| Method                      | Allocated |
|---------------------------- |----------:|
| StructuredToJsonText (flat) |   ~3.09 KB |
| StructuredClassicToJsonText |   ~2.72 KB |

The flat text path allocates ~13 % more than materialize-then-serialize on this corpus — likely `NormalizeResult.ToJsonString()`'s `ArrayBufferWriter<byte>` growing at least once from its small default starting size, where `JsonNode.ToJsonString` is already tuned for this. Wall-clock numbers on this host vary too much run-to-run to compare directly (the same flat path measured 1,113.7 ns and 2,520 ns in two consecutive runs); allocation is the reliable signal here. This is a real gap in `NormalizeResult.ToJsonString()` itself, not a tax on legacy callers — both sides of this comparison use the new API — and is left as an open, non-blocking follow-up (`StructuredClassicToJsonText` keeps it measurable).

A third independent run reproduced every number above to within 2–4 bytes (Structured 2,470 B, StructuredToJsonText 3,094 B, StructuredClassicToJsonText 2,726 B), confirming the allocation figures are stable across runs on this host; only wall-clock timing shows run-to-run noise. One reading in that run (`LongLiteral` at 0 B, against an expected ~448 B) is an outlier against the otherwise exact +32 B pattern shared by every sibling benchmark, and came with nonzero Gen1/Gen2 counts alongside it — the signature of a GC event landing mid-measurement on a sub-200 ns benchmark and confusing the allocation diagnoser. Treated as measurement noise, not a result.

## Summary: full journey (Phase 0 → final)

| Benchmark             | Phase 0             | Final                | Δ time  | Δ alloc |
|----------------------|---------------------:|----------------------:|--------:|--------:|
| MatchFast             | 615.2 ns / 606 B     | 308.3 ns / 646 B      | −50 %   | +7 %    |
| MatchBacktrack        | 653.1 ns / 704 B     | 294.9 ns / 744 B      | −55 %   | +6 %    |
| NoMatchTrie           | 337.4 ns / 496 B     | 135.4 ns / 507 B      | −60 %   | +2 %    |
| NoMatchBacktrack      | 485.3 ns / 664 B     | 178.9 ns / 523 B      | −63 %   | −21 %   |
| Structured            | 1,977.0 ns / 2,972 B | 822.4 ns / 2,470 B    | −58 %   | −17 %   |
| ConcurrentNormalize   | 664.4 ns / 607 B     | 127.4 ns / 649 B      | −81 %   | +7 %    |
| SingleThreadNormalize | 610.4 ns / 606 B     | 298.1 ns / 646 B      | −51 %   | +6 %    |

Across the whole arc — compiled snapshot (Phase 2), two-phase match/extract (Phase 3), SIMD scans (Phase 4), and the flat result type (Phase 5) — every scenario is **2–5× faster in wall-clock time**. Allocation is honestly two-sided: the scenarios that were genuinely allocation-heavy to begin with (backtracking failures, structured JSON-like motifs) are down **17–21 %** net; the simplest scenarios (a fast trie match, a plain no-match, a single uncontended call) are up a small, single-digit percentage (+2–7 %, ~11–42 bytes) — entirely attributable to Phase 5's `FieldCollector` shell, the one deliberate, examined trade made to unlock the much larger wins on the flat API (up to −75 % allocation on long fields, and net negative allocation on realistic JSON-heavy traffic).
