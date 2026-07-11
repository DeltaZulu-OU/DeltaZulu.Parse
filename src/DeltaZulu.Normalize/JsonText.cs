using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeltaZulu.Normalize;

/// <summary>
/// Helpers for parsing a single JSON value out of a larger text buffer,
/// mirroring how the C library uses json_tokener_parse_ex (which reports
/// how many characters it consumed and tolerates trailing data).
/// </summary>
internal static class JsonText
{
    /// <summary>
    /// Try to parse one JSON value starting at <paramref name="offs"/>.
    /// On success returns the parsed node (null for a JSON null literal) and
    /// the number of chars consumed, including any trailing whitespace
    /// (json-c treats whitespace after the value as part of it).
    /// </summary>
    public static bool TryParseValue(string text, int offs, out JsonNode? node, out int charsConsumed)
    {
        node = null;
        charsConsumed = 0;
        if (offs >= text.Length)
            return false;

        byte[] utf8 = Encoding.UTF8.GetBytes(text, offs, text.Length - offs);
        var reader = new Utf8JsonReader(utf8, isFinalBlock: true, state: default);
        try
        {
            node = JsonNode.Parse(ref reader, new JsonNodeOptions { PropertyNameCaseInsensitive = false });
        }
        catch (JsonException)
        {
            return false;
        }

        int bytesConsumed = (int)reader.BytesConsumed;
        charsConsumed = Encoding.UTF8.GetCharCount(utf8, 0, bytesConsumed);

        /* json-c consumes whitespace following the value; emulate that */
        while (offs + charsConsumed < text.Length && TextRules.IsSpace(text[offs + charsConsumed]))
            ++charsConsumed;
        return true;
    }

    /// <summary>Compact serialization (no added whitespace), like json-c's PLAIN mode.</summary>
    public static string ToCompactString(JsonNode? node)
        => node?.ToJsonString(SerializerOptions) ?? "null";

    /// <summary>
    /// Read an integer the way json-c's json_object_get_int64 does: numbers
    /// convert directly, but a JSON *string* is also accepted and parsed as
    /// a number (rulebases sometimes quote numeric parameters, e.g.
    /// "priority":"1000"). Unparseable or missing values yield 0, matching
    /// json-c's lenient "return 0 on failure" behavior rather than throwing.
    /// </summary>
    public static long GetLenientInt64(JsonNode? node)
    {
        if (node is not JsonValue v)
            return 0;
        if (v.TryGetValue(out long l))
            return l;
        if (v.TryGetValue(out double d))
            return (long)d;
        if (v.TryGetValue(out string? s) && long.TryParse(s, out long parsed))
            return parsed;
        return 0;
    }

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
