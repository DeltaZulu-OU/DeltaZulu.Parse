using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;

namespace DeltaZulu.Parse.Benchmarks;

/// <summary>
/// Hot-path benchmarks. Contexts are built once in GlobalSetup; the benchmark
/// bodies only call Parse. Iterating over a small message set per invoke
/// keeps the branch predictor honest; time is reported per single Parse
/// call via OperationsPerInvoke.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ParsingBenchmarks
{
    private static readonly System.Text.Json.JsonSerializerOptions ClassicSerializerOptions = new() {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private ParseContext _backtrackCtx = null!;
    private string[] _backtrackMatch = null!;
    private string[] _backtrackNoMatch = null!;
    private ParseContext _structuredCtx = null!;
    private string[] _structuredMatch = null!;
    private ParseContext _trieCtx = null!;
    private string[] _trieMatch = null!;
    private string[] _trieNoMatch = null!;

    [Benchmark(OperationsPerInvoke = 3)]
    public int MatchBacktrack() => RunAll(_backtrackCtx, _backtrackMatch);

    [Benchmark(OperationsPerInvoke = 29)] /* 29 matching messages generated for 200 rules */
    public int MatchFast() => RunAll(_trieCtx, _trieMatch);

    [Benchmark(OperationsPerInvoke = 3)]
    public int NoMatchBacktrack() => RunAll(_backtrackCtx, _backtrackNoMatch);

    [Benchmark(OperationsPerInvoke = 3)]
    public int NoMatchTrie() => RunAll(_trieCtx, _trieNoMatch);

    [GlobalSetup]
    public void Setup()
    {
        _trieCtx = Load(BenchmarkRulebases.TrieHeavy(200, out _trieMatch, out _trieNoMatch));
        _backtrackCtx = Load(BenchmarkRulebases.BacktrackHeavy(out _backtrackMatch, out _backtrackNoMatch));
        _structuredCtx = Load(BenchmarkRulebases.Structured(out _structuredMatch));

        /* warm/compile each context outside measurement, and verify the
         * corpus does what its name claims — a benchmark on silently
         * non-matching messages would measure the wrong scenario */
        AssertAll(_trieCtx, _trieMatch, shouldMatch: true);
        AssertAll(_trieCtx, _trieNoMatch, shouldMatch: false);
        AssertAll(_backtrackCtx, _backtrackMatch, shouldMatch: true);
        AssertAll(_backtrackCtx, _backtrackNoMatch, shouldMatch: false);
        AssertAll(_structuredCtx, _structuredMatch, shouldMatch: true);
    }

    [Benchmark(OperationsPerInvoke = 4)]
    public int Structured() => RunAll(_structuredCtx, _structuredMatch);

    /// <summary>
    /// What ParseToString cost before it was rewritten over the flat
    /// result (materialize a JsonObject, then serialize it) — the reference
    /// point StructuredToJsonText must beat to justify running the walk
    /// through the flat path at all when the caller wants text.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 4)]
    public int StructuredClassicToJsonText()
    {
        var r = 0;
        foreach (var msg in _structuredMatch)
        {
            r += _structuredCtx.Parse(msg, out JsonObject json);
            _ = json.ToJsonString(ClassicSerializerOptions);
        }

        return r;
    }

    [Benchmark(OperationsPerInvoke = 4)]
    public int StructuredFlatOnly()
    {
        var r = 0;
        foreach (var msg in _structuredMatch)
        {
            r += _structuredCtx.Parse(msg, out ParseResult _);
        }

        return r;
    }

    [Benchmark(OperationsPerInvoke = 4)]
    public int StructuredToJsonText()
    {
        var r = 0;
        foreach (var msg in _structuredMatch)
        {
            r += _structuredCtx.ParseToString(msg, out _);
        }

        return r;
    }

    private static void AssertAll(ParseContext ctx, string[] messages, bool shouldMatch)
    {
        foreach (var msg in messages)
        {
            var matched = ctx.Parse(msg, out JsonObject _) == 0;
            if (matched != shouldMatch)
            {
                throw new InvalidOperationException(
                    $"corpus error: '{msg}' {(matched ? "matched" : "did not match")} but should{(shouldMatch ? string.Empty : " not")}");
            }
        }
    }

    private static ParseContext Load(string rulebase)
    {
        var ctx = new ParseContext {
            ErrorCallback = msg => throw new InvalidOperationException($"rulebase error: {msg}")
        };
        if (ctx.LoadSamplesFromString(rulebase) != 0)
        {
            throw new InvalidOperationException("rulebase load failed");
        }

        return ctx;
    }

    private static int RunAll(ParseContext ctx, string[] messages)
    {
        var r = 0;
        foreach (var msg in messages)
        {
            r += ctx.Parse(msg, out JsonObject _);
        }

        return r;
    }

    /* flat-result variants of Structured: no JsonObject is ever built */
    /* mirrors the library's internal JsonText.SerializerOptions (not visible
     * across the assembly boundary), so this is an apples-to-apples compact
     * serialization for comparison */
}

/// <summary>
/// Multithreaded throughput on one shared context: exposes the per-node
/// statistics counters' cache-line contention (shared writes on every node
/// visit). OperationsPerInvoke = messages per invocation so the score is
/// per-Parse-call.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ConcurrentBenchmarks
{
    private const int MessagesPerInvoke = 2000;

    private ParseContext _ctx = null!;
    private string[] _messages = null!;

    [Benchmark(OperationsPerInvoke = MessagesPerInvoke)]
    public void ConcurrentParse()
    {
        var partitioner = Partitioner.Create(
            0,
            MessagesPerInvoke,
            Math.Max(1, MessagesPerInvoke / (Environment.ProcessorCount * 4))
        );

        Parallel.ForEach(partitioner, new ParallelOptions {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        },
        range => {
            for (var i = range.Item1; i < range.Item2; i++)
            {
                _ctx.Parse(_messages[i], out JsonObject _);
            }
        });
    }

    [GlobalSetup]
    public void Setup()
    {
        var ctx = new ParseContext {
            ErrorCallback = msg => throw new InvalidOperationException($"rulebase error: {msg}")
        };
        var rb = BenchmarkRulebases.TrieHeavy(200, out var match, out _);
        if (ctx.LoadSamplesFromString(rb) != 0)
        {
            throw new InvalidOperationException("rulebase load failed");
        }

        _ctx = ctx;
        _messages = new string[MessagesPerInvoke];
        for (var i = 0; i < MessagesPerInvoke; i++)
        {
            _messages[i] = match[i % match.Length];
        }

        _ctx.Parse(_messages[0], out JsonObject _);
    }

    [Benchmark(OperationsPerInvoke = MessagesPerInvoke)]
    public void SingleThreadParse()
    {
        for (var i = 0; i < MessagesPerInvoke; i++)
        {
            _ctx.Parse(_messages[i], out JsonObject _);
        }
    }
}
