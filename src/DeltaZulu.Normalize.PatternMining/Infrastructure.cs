using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DeltaZulu.Normalize.PatternMining;

public sealed class FileLogSource : IReplayableLogSource
{
    private readonly IReadOnlyList<string> _paths;

    public FileLogSource(IEnumerable<string> paths)
    {
        _paths = paths.Select(Path.GetFullPath).ToArray();
        if (_paths.Count == 0)
            throw new ArgumentException("At least one input file is required.", nameof(paths));
    }

    public async IAsyncEnumerable<LogRecord> ReadPassAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        long sequence = 0;
        foreach (var path in _paths)
        {
            await foreach (var line in File.ReadLinesAsync(path, cancellationToken))
                yield return new LogRecord(++sequence, line, path);
        }
    }
}

public sealed class MemoryLogSource(IEnumerable<string> lines) : IReplayableLogSource
{
    private readonly string[] _lines = lines.ToArray();

    public async IAsyncEnumerable<LogRecord> ReadPassAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < _lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new LogRecord(i + 1, _lines[i]);
            await Task.Yield();
        }
    }
}

public sealed class RegexLogTokenizer : ILogTokenizer
{
    private readonly Regex _separator;

    public RegexLogTokenizer(string separatorPattern = @"\s+")
    {
        _separator = new Regex(separatorPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    }

    public IReadOnlyList<string> Tokenize(string message) =>
        _separator.Split(message).Where(static token => token.Length > 0).ToArray();
}

public sealed class SyntaxRegistry
{
    private readonly ISyntaxRecognizer[] _recognizers;

    public SyntaxRegistry(IEnumerable<ISyntaxRecognizer>? recognizers = null)
    {
        _recognizers = (recognizers ?? CreateDefault()).OrderByDescending(x => x.Specificity).ToArray();
    }

    public IReadOnlyList<string> Match(string value)
    {
        var matches = new List<string>();
        foreach (var recognizer in _recognizers)
            if (recognizer.IsMatch(value.AsSpan()))
                matches.Add(recognizer.Name);
        return matches;
    }

    public ISyntaxRecognizer? Resolve(string name) =>
        _recognizers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));

    private static IEnumerable<ISyntaxRecognizer> CreateDefault()
    {
        yield return new JsonRecognizer();
        yield return new UuidRecognizer();
        yield return new IpRecognizer("ipv4", AddressFamily.InterNetwork, 100);
        yield return new IpRecognizer("ipv6", AddressFamily.InterNetworkV6, 100);
        yield return new MacAddressRecognizer();
        yield return new IsoTimestampRecognizer();
        yield return new Time24Recognizer();
        yield return new PositiveIntegerRecognizer();
        yield return new IntegerRecognizer();
        yield return new NumberRecognizer();
        yield return new WordRecognizer();
    }
}

public sealed class IpRecognizer(string name, AddressFamily family, int specificity) : ISyntaxRecognizer
{
    public string Name { get; } = name;
    public int Specificity { get; } = specificity;
    public bool IsMatch(ReadOnlySpan<char> value) => IPAddress.TryParse(value, out var address) && address.AddressFamily == family;
}

public sealed class UuidRecognizer : ISyntaxRecognizer
{
    public string Name => "uuid";
    public int Specificity => 110;
    public bool IsMatch(ReadOnlySpan<char> value) => Guid.TryParse(value, out _);
}

public sealed partial class MacAddressRecognizer : ISyntaxRecognizer
{
    public string Name => "mac48";
    public int Specificity => 105;
    public bool IsMatch(ReadOnlySpan<char> value) => MacRegex().IsMatch(value);
    [GeneratedRegex(@"^(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex MacRegex();
}

public sealed class PositiveIntegerRecognizer : ISyntaxRecognizer
{
    public string Name => "posint";
    public int Specificity => 80;
    public bool IsMatch(ReadOnlySpan<char> value) => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result > 0;
}

public sealed class IntegerRecognizer : ISyntaxRecognizer
{
    public string Name => "integer";
    public int Specificity => 70;
    public bool IsMatch(ReadOnlySpan<char> value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
}

public sealed class NumberRecognizer : ISyntaxRecognizer
{
    public string Name => "number";
    public int Specificity => 60;
    public bool IsMatch(ReadOnlySpan<char> value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
}

public sealed partial class Time24Recognizer : ISyntaxRecognizer
{
    public string Name => "time-24hr";
    public int Specificity => 90;
    public bool IsMatch(ReadOnlySpan<char> value) => TimeRegex().IsMatch(value);
    [GeneratedRegex(@"^(?:[01]\d|2[0-3]):[0-5]\d(?::[0-5]\d(?:\.\d+)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeRegex();
}

public sealed class IsoTimestampRecognizer : ISyntaxRecognizer
{
    public string Name => "date-iso";
    public int Specificity => 95;
    public bool IsMatch(ReadOnlySpan<char> value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out _);
}

public sealed class JsonRecognizer : ISyntaxRecognizer
{
    public string Name => "json";
    public int Specificity => 120;
    public bool IsMatch(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || (value[0] != '{' && value[0] != '['))
            return false;
        try
        {
            using var document = JsonDocument.Parse(value.ToString());
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed partial class WordRecognizer : ISyntaxRecognizer
{
    public string Name => "word";
    public int Specificity => 10;
    public bool IsMatch(ReadOnlySpan<char> value) => WordRegex().IsMatch(value);
    [GeneratedRegex(@"^\S+$", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
