namespace DeltaZulu.Parse;

/// <summary>
/// The parse parameter block (npb, named after liblognorm's own "normalization
/// parameter block"): per-message state shared by the recursive walker and
/// all motif parsers. The PDAG itself stays read-only during parsing;
/// everything mutable lives here.
/// </summary>
internal sealed class Npb
{
    /// <summary>The furthest position any attempted path ever reached (for "unparsed-data").</summary>
    public int LongestParsedTo;

    /// <summary>Up to which position the current (successful) parse path consumed input.</summary>
    public int ParsedTo;

    /// <summary>
    /// Mock-up of the matching rule (only populated with <see cref="ParseOptions.AddRule"/>).
    /// Segments are appended while unwinding the recursion, i.e. deepest-first;
    /// they are reversed when the rule string is emitted.
    /// </summary>
    public List<string>? RuleSegments;

    public required ParseContext Ctx { get; init; }

    /// <summary>The compiled rulebase snapshot this message is parsed against.
    /// Carried here so nested walks (custom types, "repeat") reach the same
    /// snapshot even while the context is being reloaded concurrently.</summary>
    public required CompiledPdag Snap { get; init; }

    /// <summary>The message being parsed.</summary>
    public required string Str { get; init; }

    /// <summary>Length of <see cref="Str"/> (kept explicit to mirror the C code).</summary>
    public int StrLen => Str.Length;

    /// <summary>Character at <paramref name="i"/>, or NUL past the end.
    /// Mirrors the C library reading its NUL-terminated buffer at str[strLen].</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public char At(int i) => i < Str.Length ? Str[i] : '\0';
}
