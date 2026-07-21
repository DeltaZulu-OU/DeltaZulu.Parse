namespace DeltaZulu.Parse;

/// <summary>How a built-in motif should be used by rule-suggestion tooling.</summary>
public enum ParserSuggestionUse
{
    None,
    InferFromSample,
    FallbackOnly,
}

/// <summary>Public metadata for one built-in motif parser.</summary>
public sealed record ParserDescriptor(
    string Name,
    int Priority,
    ParserSuggestionUse SuggestionUse,
    bool RequiresConfiguration)
{
    public bool CanInferFromSample => SuggestionUse == ParserSuggestionUse.InferFromSample;

    public bool CanRenderWithoutConfiguration => !RequiresConfiguration;
}

/// <summary>
/// Catalog of the built-in motif parsers that are useful to external
/// tooling, such as rule editors and parser-suggestion experiences.
/// </summary>
public interface IParserCatalog
{
    IReadOnlyList<ParserDescriptor> Parsers { get; }

    string WordParserName { get; }

    string RestParserName { get; }

    bool TryGetParser(string name, out ParserDescriptor parser);

    bool IsFullMatch(string parserName, ReadOnlySpan<char> sample);
}

/// <summary>Default catalog for DeltaZulu.Parse's built-in motif parsers.</summary>
public sealed class ParserCatalog : IParserCatalog
{
    public static IParserCatalog Instance { get; } = new ParserCatalog();

    public IReadOnlyList<ParserDescriptor> Parsers => ParserTable.CatalogParsers;

    public string WordParserName => ParserTable.WordParserName;

    public string RestParserName => ParserTable.RestParserName;

    public bool TryGetParser(string name, out ParserDescriptor parser)
        => ParserTable.TryGetCatalogParser(name, out parser);

    public bool IsFullMatch(string parserName, ReadOnlySpan<char> sample) =>
        ParserTable.IsCatalogFullMatch(parserName, sample);
}
