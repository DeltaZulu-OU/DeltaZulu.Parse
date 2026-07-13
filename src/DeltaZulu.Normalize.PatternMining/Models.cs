using System.Collections.Immutable;

namespace DeltaZulu.Normalize.PatternMining;

public sealed record LogRecord(long Sequence, string Text, string? Source = null);

public interface IReplayableLogSource
{
    IAsyncEnumerable<LogRecord> ReadPassAsync(CancellationToken cancellationToken = default);
}

public interface ILogTokenizer
{
    IReadOnlyList<string> Tokenize(string message);
}

public interface ISyntaxRecognizer
{
    string Name { get; }
    int Specificity { get; }
    bool IsMatch(ReadOnlySpan<char> value);
}

public readonly record struct GapRange(int Minimum, int Maximum)
{
    public static GapRange FromValue(int value) => new(value, value);
    public GapRange Observe(int value) => new(Math.Min(Minimum, value), Math.Max(Maximum, value));
}

public readonly struct CandidateKey : IEquatable<CandidateKey>
{
    private readonly ImmutableArray<int> _anchors;
    private readonly int _hashCode;

    public CandidateKey(IEnumerable<int> anchors)
    {
        _anchors = anchors.ToImmutableArray();
        if (_anchors.IsDefaultOrEmpty)
            throw new ArgumentException("A candidate key requires at least one anchor.", nameof(anchors));

        var hash = new HashCode();
        foreach (var anchor in _anchors)
            hash.Add(anchor);
        _hashCode = hash.ToHashCode();
    }

    public ImmutableArray<int> Anchors => _anchors;
    public bool Equals(CandidateKey other) => _anchors.AsSpan().SequenceEqual(other._anchors.AsSpan());
    public override bool Equals(object? obj) => obj is CandidateKey other && Equals(other);
    public override int GetHashCode() => _hashCode;
}

public sealed class ReservoirSampler<T>
{
    private readonly T[] _items;
    private readonly Random _random;
    private long _seen;
    private int _count;

    public ReservoirSampler(int capacity, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _items = new T[capacity];
        _random = new Random(seed);
    }

    public long Seen => _seen;
    public IReadOnlyList<T> Items => new ArraySegment<T>(_items, 0, _count);

    public void Add(T item)
    {
        _seen++;
        if (_items.Length == 0)
            return;
        if (_count < _items.Length)
        {
            _items[_count++] = item;
            return;
        }

        var index = _random.NextInt64(_seen);
        if (index < _items.Length)
            _items[index] = item;
    }
}

public sealed class GapStatistics
{
    private readonly Dictionary<int, long> _lengthHistogram = [];
    private readonly Dictionary<string, long> _syntaxMatches = new(StringComparer.Ordinal);

    public GapStatistics(int initialLength, int sampleCapacity, int seed)
    {
        Range = GapRange.FromValue(initialLength);
        Samples = new ReservoirSampler<string>(sampleCapacity, seed);
        ObserveLength(initialLength);
    }

    public GapRange Range { get; private set; }
    public long ObservationCount { get; private set; }
    public IReadOnlyDictionary<int, long> LengthHistogram => _lengthHistogram;
    public IReadOnlyDictionary<string, long> SyntaxMatches => _syntaxMatches;
    public ReservoirSampler<string> Samples { get; }

    public void ObserveLength(int wordCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wordCount);
        if (ObservationCount > 0)
            Range = Range.Observe(wordCount);
        _lengthHistogram.TryGetValue(wordCount, out var count);
        _lengthHistogram[wordCount] = count + 1;
        ObservationCount++;
    }

    public void ObserveSample(string value, IEnumerable<string> matchedSyntaxes)
    {
        Samples.Add(value);
        foreach (var syntax in matchedSyntaxes)
        {
            _syntaxMatches.TryGetValue(syntax, out var count);
            _syntaxMatches[syntax] = count + 1;
        }
    }
}

public sealed class PatternCandidate
{
    public required CandidateKey Key { get; init; }
    public required int[] AnchorIds { get; init; }
    public required GapStatistics[] Gaps { get; init; }
    public long RawSupport { get; set; }
    public CandidateScore Score { get; set; } = CandidateScore.Empty;
    public required ReservoirSampler<string> Examples { get; init; }
}

public sealed record CandidateScore(double Total, double SupportStrength, double AnchorQuality, double GapConsistency, double Specificity)
{
    public static CandidateScore Empty { get; } = new(0, 0, 0, 0, 0);
}

public sealed record PatternSuggestion(PatternCandidate Candidate, string LogClusterPattern, string LiblognormSuggestion);

public sealed record MiningOptions
{
    public long? Support { get; init; }
    public double RelativeSupportPercent { get; init; } = 0.1;
    public double MinimumScore { get; init; }
    public int SampleSizePerGap { get; init; } = 128;
    public int ExampleCount { get; init; } = 3;
    public double SyntaxCoverageThreshold { get; init; } = 0.95;
    public int? Top { get; init; }
}

public sealed record MiningResult(long TotalRecords, long SupportThreshold, IReadOnlyDictionary<string, long> FrequentWords, IReadOnlyList<PatternSuggestion> Suggestions);
