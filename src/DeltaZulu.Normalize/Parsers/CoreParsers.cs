using System.Text;
using System.Text.Json.Nodes;

namespace DeltaZulu.Normalize.Parsers;

/// <summary>
/// Simple text motifs: whitespace, word, alpha, rest, string-to, char-to,
/// char-sep and the quoted-string variants.
/// </summary>
internal static class CoreParsers
{
    /// <summary>All whitespace up to the first non-whitespace char; must start on whitespace.</summary>
    public static int ParseWhitespace(Npb npb, ref int offs, object? pdata, string? parserName,
        out int parsed, bool wantValue, ref JsonNode? value)
    {
        parsed = 0;
        int i = offs;
        if (!TextRules.IsSpace(npb.At(i)))
            return ErrorCodes.WrongParser;
        for (i++; i < npb.StrLen && TextRules.IsSpace(npb.Str[i]); i++) { }
        parsed = i - offs;
        if (wantValue)
            value = JsonValue.Create(npb.Str.Substring(offs, parsed));
        return 0;
    }

    /// <summary>A space-delimited entity (fails only when positioned on a space).</summary>
    public static int ParseWord(Npb npb, ref int offs, object? pdata, string? parserName,
        out int parsed, bool wantValue, ref JsonNode? value)
    {
        parsed = 0;
        int i = offs;
        while (i < npb.StrLen && npb.Str[i] != ' ')
            i++;
        if (i == offs)
            return ErrorCodes.WrongParser;
        parsed = i - offs;
        if (wantValue)
            value = JsonValue.Create(npb.Str.Substring(offs, parsed));
        return 0;
    }

    /// <summary>A run of alphabetic characters.</summary>
    public static int ParseAlpha(Npb npb, ref int offs, object? pdata, string? parserName,
        out int parsed, bool wantValue, ref JsonNode? value)
    {
        parsed = 0;
        int i = offs;
        while (i < npb.StrLen && TextRules.IsAlpha(npb.Str[i]))
            i++;
        if (i == offs)
            return ErrorCodes.WrongParser;
        parsed = i - offs;
        if (wantValue)
            value = JsonValue.Create(npb.Str.Substring(offs, parsed));
        return 0;
    }

    /// <summary>Everything to end-of-string; always succeeds (even consuming zero chars).</summary>
    public static int ParseRest(Npb npb, ref int offs, object? pdata, string? parserName,
        out int parsed, bool wantValue, ref JsonNode? value)
    {
        parsed = npb.StrLen - offs;
        if (wantValue)
            value = JsonValue.Create(npb.Str.Substring(offs, parsed));
        return 0;
    }

    /* ---------- string-to ---------- */

    internal sealed class StringToData
    {
        public required string ToFind { get; init; }
    }

    public static int ConstructStringTo(LogNormContext ctx, JsonObject config, out object? pdata)
    {
        pdata = null;
        if (config["extradata"] is not JsonValue ed)
        {
            ctx.Error("string-to type needs 'extradata' parameter");
            return ErrorCodes.BadConfig;
        }
        string toFind = ed.GetValue<string>();
        if (toFind.Length == 0)
        {
            ctx.Error("string-to type needs non-empty 'extradata' parameter");
            return ErrorCodes.BadConfig;
        }
        pdata = new StringToData { ToFind = toFind };
        return 0;
    }

    /// <summary>Everything up to a specific search string.</summary>
    public static int ParseStringTo(Npb npb, ref int offs, object? pdata, string? parserName,
        out int parsed, bool wantValue, ref JsonNode? value)
    {
        parsed = 0;
        string toFind = ((StringToData)pdata!).ToFind;
        int i = offs;
        bool found = false;

        /* hunt for the first letter, then check the rest of the string;
         * note the parser can never match an empty prefix (i++ first) */
        while (!found && i < npb.StrLen)
        {
            i++;
            if (npb.At(i) == toFind[0])
            {
                int j = 1;
                int m = i + 1;
                while (m < npb.StrLen && j < toFind.Length)
                {
                    if (npb.Str[m] != toFind[j])
                        break;
                    if (j == toFind.Length - 1)
                    {
                        found = true;
                        break;
                    }
                    j++;
                    m++;
                }
            }
        }
        if (i == offs || i == npb.StrLen || !found)
            return ErrorCodes.WrongParser;

        parsed = i - offs;
        if (wantValue)
            value = JsonValue.Create(npb.Str.Substring(offs, parsed));
        return 0;
    }

    /* ---------- char-to ---------- */

    internal sealed class CharToData
    {
        public required string TermChars { get; init; }
    }

    public static int ConstructCharTo(LogNormContext ctx, JsonObject config, out object? pdata)
    {
        pdata = null;
        if (config["extradata"] is not JsonValue ed)
        {
            ctx.Error("char-to type needs 'extradata' parameter");
            return ErrorCodes.BadConfig;
        }
        pdata = new CharToData { TermChars = ed.GetValue<string>() };
        return 0;
    }

    /// <summary>Everything up to one of a set of terminator characters, which must be present.</summary>
    public static int ParseCharTo(Npb npb, ref int offs, object? pdata, string? parserName,
        out int parsed, bool wantValue, ref JsonNode? value)
    {
        parsed = 0;
        var data = (CharToData)pdata!;
        int i = offs;
        bool found = false;
        while (i < npb.StrLen && !found)
        {
            if (data.TermChars.Contains(npb.Str[i]))
                found = true;
            else
                ++i;
        }
        if (i == offs || i == npb.StrLen || !found)
            return ErrorCodes.WrongParser;

        parsed = i - offs;
        if (wantValue)
            value = JsonValue.Create(npb.Str.Substring(offs, parsed));
        return 0;
    }

    /* ---------- char-sep ---------- */

    internal sealed class CharSeparatedData
    {
        public required string TermChars { get; init; }
    }

    public static int ConstructCharSeparated(LogNormContext ctx, JsonObject config, out object? pdata)
    {
        pdata = null;
        if (config["extradata"] is not JsonValue ed)
        {
            ctx.Error("char-separated type needs 'extradata' parameter");
            return ErrorCodes.BadConfig;
        }
        pdata = new CharSeparatedData { TermChars = ed.GetValue<string>() };
        return 0;
    }

    /// <summary>Everything up to a terminator char or end-of-string; always succeeds.</summary>
    public static int ParseCharSeparated(Npb npb, ref int offs, object? pdata, string? parserName,
        out int parsed, bool wantValue, ref JsonNode? value)
    {
        var data = (CharSeparatedData)pdata!;
        int i = offs;
        bool found = false;
        while (i < npb.StrLen && !found)
        {
            if (data.TermChars.Contains(npb.Str[i]))
                found = true;
            else
                ++i;
        }
        parsed = i - offs;
        if (wantValue)
            value = JsonValue.Create(npb.Str.Substring(offs, parsed));
        return 0;
    }

    /* ---------- quoted strings ---------- */

    /// <summary>A double-quoted string without escape support; quotes are stripped.</summary>
    public static int ParseQuotedString(Npb npb, ref int offs, object? pdata, string? parserName,
        out int parsed, bool wantValue, ref JsonNode? value)
    {
        parsed = 0;
        int i = offs;
        if (i + 2 > npb.StrLen)
            return ErrorCodes.WrongParser;
        if (npb.Str[i] != '"')
            return ErrorCodes.WrongParser;
        ++i;
        while (i < npb.StrLen && npb.Str[i] != '"')
            i++;
        if (i == npb.StrLen)
            return ErrorCodes.WrongParser;

        parsed = i + 1 - offs; /* "eat" terminal double quote */
        if (wantValue)
            value = JsonValue.Create(npb.Str.Substring(offs + 1, parsed - 2));
        return 0;
    }

    internal sealed class OpQuotedStringData
    {
        public bool Escape;
    }

    public static int ConstructOpQuotedString(LogNormContext ctx, JsonObject config, out object? pdata)
    {
        pdata = null;
        var data = new OpQuotedStringData();
        if (config.TryGetPropertyValue("escape", out JsonNode? obj))
        {
            if (obj is JsonValue v && v.TryGetValue(out bool b))
            {
                data.Escape = b;
            }
            else
            {
                ctx.Error("op-quoted-string's 'escape' field should be boolean");
                return ErrorCodes.BadConfig;
            }
        }
        pdata = data;
        return 0;
    }

    /// <summary>
    /// An optionally quoted string: either a space-delimited word, or a
    /// double-quoted string (quotes stripped; escape handling per config).
    /// </summary>
    public static int ParseOpQuotedString(Npb npb, ref int offs, object? pdata, string? parserName,
        out int parsed, bool wantValue, ref JsonNode? value)
    {
        parsed = 0;
        bool escape = pdata is OpQuotedStringData d && d.Escape;
        int i = offs;
        string cstr;

        if (i == npb.StrLen)
            return ErrorCodes.WrongParser;

        if (npb.Str[i] != '"')
        {
            while (i < npb.StrLen && npb.Str[i] != ' ')
                i++;
            if (i == offs)
                return ErrorCodes.WrongParser;
            parsed = i - offs;
            cstr = npb.Str.Substring(offs, parsed);
        }
        else if (escape)
        {
            ++i;
            /* the closing quote must not be escaped; a backslash escapes
             * itself, so only an odd run of backslashes escapes the quote */
            int continuousBackslash = 0;
            while (i < npb.StrLen && (npb.Str[i] != '"' || (continuousBackslash & 1) == 1))
            {
                if (npb.Str[i] == '\\')
                    continuousBackslash++;
                else
                    continuousBackslash = 0;
                ++i;
            }
            if (i == npb.StrLen || npb.Str[i] != '"')
                return ErrorCodes.WrongParser;

            int end = i;
            i = offs + 1; /* eat starting quote */
            var sb = new StringBuilder(end - i);
            while (i < end)
            {
                if (npb.Str[i] == '\\' && (npb.At(i + 1) == '\\' || npb.At(i + 1) == '"'))
                    i++;
                sb.Append(npb.Str[i++]);
            }
            cstr = sb.ToString();
            parsed = i + 1 - offs; /* "eat" terminal double quote */
        }
        else
        {
            ++i;
            while (i < npb.StrLen && npb.Str[i] != '"')
                i++;
            if (i == npb.StrLen)
                return ErrorCodes.WrongParser;
            parsed = i + 1 - offs;
            cstr = npb.Str.Substring(offs + 1, parsed - 2);
        }

        if (wantValue)
            value = JsonValue.Create(cstr);
        return 0;
    }
}
