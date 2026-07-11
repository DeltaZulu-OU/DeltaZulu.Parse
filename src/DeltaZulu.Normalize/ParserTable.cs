using System.Text.Json.Nodes;
using DeltaZulu.Normalize.Parsers;

namespace DeltaZulu.Normalize;

/// <summary>
/// Parse function of a motif parser.
/// </summary>
/// <param name="npb">the normalization parameter block (message + state)</param>
/// <param name="offs">offset into the message where matching must start</param>
/// <param name="pdata">parser-specific configuration data (from construct)</param>
/// <param name="parserName">field name of this parser instance (used by repeat)</param>
/// <param name="parsed">number of chars consumed on success</param>
/// <param name="wantValue">false when the value would be discarded (unnamed field);
/// parsers may then skip extraction entirely</param>
/// <param name="value">extracted value (only meaningful on success and when wanted)</param>
/// <returns>0 on success, <see cref="ErrorCodes.WrongParser"/> when the motif does not match</returns>
internal delegate int ParseFunc(
    Npb npb, ref int offs, object? pdata, string? parserName,
    out int parsed, bool wantValue, ref JsonNode? value);

/// <summary>
/// Builds parser-specific data from the (already reduced) JSON config of a
/// field. Returns 0 on success, <see cref="ErrorCodes.BadConfig"/> otherwise.
/// </summary>
internal delegate int ConstructFunc(LogNormContext ctx, JsonObject config, out object? pdata);

/// <summary>Static description of one motif parser type.</summary>
internal sealed class ParserInfo
{
    /// <summary>Parser name as used in the rulebase.</summary>
    public required string Name { get; init; }

    /// <summary>Parser-specific priority (0 = highest .. 255 = lowest / last resort).</summary>
    public required int Priority { get; init; }

    public ConstructFunc? Construct { get; init; }
    public required ParseFunc Parse { get; init; }
}

/// <summary>
/// The parser lookup table. Parser IDs are indexes into <see cref="Parsers"/>;
/// the literal parser MUST be entry 0 and repeat entry 1 (the engine
/// special-cases both by ID).
/// </summary>
internal static class ParserTable
{
    public const byte LiteralId = 0;
    public const byte RepeatId = 1;
    public const byte CustomTypeId = 254;
    public const byte InvalidId = 255;

    /// <summary>Default user priority when a rule does not assign one.</summary>
    public const int DefaultUserPriority = 30000;

    /// <summary>Priority used for user-defined (custom) types.</summary>
    public const int CustomTypePriority = 16;

    public static readonly ParserInfo[] Parsers =
    {
        new() { Name = "literal", Priority = 4, Construct = LiteralParser.Construct, Parse = LiteralParser.Parse },
        new() { Name = "repeat", Priority = 4, Construct = RepeatParser.Construct, Parse = RepeatParser.Parse },
        new() { Name = "date-rfc3164", Priority = 8, Construct = DateTimeParsers.ConstructRfc3164, Parse = DateTimeParsers.ParseRfc3164 },
        new() { Name = "date-rfc5424", Priority = 8, Construct = DateTimeParsers.ConstructRfc5424, Parse = DateTimeParsers.ParseRfc5424 },
        new() { Name = "number", Priority = 16, Construct = NumberParsers.ConstructNumber, Parse = NumberParsers.ParseNumber },
        new() { Name = "float", Priority = 16, Construct = NumberParsers.ConstructFloat, Parse = NumberParsers.ParseFloat },
        new() { Name = "hexnumber", Priority = 16, Construct = NumberParsers.ConstructHexNumber, Parse = NumberParsers.ParseHexNumber },
        new() { Name = "kernel-timestamp", Priority = 16, Parse = DateTimeParsers.ParseKernelTimestamp },
        new() { Name = "whitespace", Priority = 4, Parse = CoreParsers.ParseWhitespace },
        new() { Name = "ipv4", Priority = 4, Parse = NetworkParsers.ParseIPv4 },
        new() { Name = "ipv6", Priority = 4, Parse = NetworkParsers.ParseIPv6 },
        new() { Name = "word", Priority = 32, Parse = CoreParsers.ParseWord },
        new() { Name = "alpha", Priority = 32, Parse = CoreParsers.ParseAlpha },
        new() { Name = "rest", Priority = 255, Parse = CoreParsers.ParseRest },
        new() { Name = "op-quoted-string", Priority = 64, Construct = CoreParsers.ConstructOpQuotedString, Parse = CoreParsers.ParseOpQuotedString },
        new() { Name = "quoted-string", Priority = 64, Parse = CoreParsers.ParseQuotedString },
        new() { Name = "date-iso", Priority = 8, Parse = DateTimeParsers.ParseIsoDate },
        new() { Name = "time-24hr", Priority = 8, Parse = DateTimeParsers.ParseTime24hr },
        new() { Name = "time-12hr", Priority = 8, Parse = DateTimeParsers.ParseTime12hr },
        new() { Name = "duration", Priority = 16, Parse = DateTimeParsers.ParseDuration },
        new() { Name = "cisco-interface-spec", Priority = 4, Parse = NetworkParsers.ParseCiscoInterfaceSpec },
        new() { Name = "json", Priority = 4, Construct = StructuredParsers.ConstructJson, Parse = StructuredParsers.ParseJson },
        new() { Name = "cee-syslog", Priority = 4, Parse = StructuredParsers.ParseCeeSyslog },
        new() { Name = "mac48", Priority = 16, Parse = NetworkParsers.ParseMac48 },
        new() { Name = "cef", Priority = 4, Parse = StructuredParsers.ParseCef },
        new() { Name = "v2-iptables", Priority = 4, Parse = StructuredParsers.ParseV2IpTables },
        new() { Name = "name-value-list", Priority = 8, Construct = StructuredParsers.ConstructNameValue, Parse = StructuredParsers.ParseNameValue },
        new() { Name = "checkpoint-lea", Priority = 4, Construct = StructuredParsers.ConstructCheckpointLea, Parse = StructuredParsers.ParseCheckpointLea },
        new() { Name = "string-to", Priority = 32, Construct = CoreParsers.ConstructStringTo, Parse = CoreParsers.ParseStringTo },
        new() { Name = "char-to", Priority = 32, Construct = CoreParsers.ConstructCharTo, Parse = CoreParsers.ParseCharTo },
        new() { Name = "char-sep", Priority = 32, Construct = CoreParsers.ConstructCharSeparated, Parse = CoreParsers.ParseCharSeparated },
        new() { Name = "string", Priority = 32, Construct = StringParser.Construct, Parse = StringParser.Parse },
    };

    public static byte NameToId(string name)
    {
        for (int i = 0; i < Parsers.Length; ++i)
        {
            if (Parsers[i].Name == name)
                return (byte)i;
        }
        return InvalidId;
    }

    public static string IdToName(byte id)
        => id == CustomTypeId ? "USER-DEFINED" : Parsers[id].Name;
}
