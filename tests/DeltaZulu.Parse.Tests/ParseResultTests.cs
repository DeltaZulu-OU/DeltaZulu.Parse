using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeltaZulu.Parse.Tests;

[TestClass]
public class ParseResultTests
{
    [TestMethod]
    public void EagerStructuredValues_SurviveTheFlatPath()
    {
        var ctx = Load("rule=:data %fields:json%");
        ctx.Parse("""data {"a": 1, "b": [true, null]}""", out ParseResult result);

        Assert.IsTrue(result.Matched);
        Assert.IsFalse(result.TryGetRawText("fields", out _), "json motif is not a raw slice");
        var obj = result.ToJsonObject();
        Assert.AreEqual(1, obj["fields"]!["a"]!.GetValue<int>());
        Assert.IsTrue(obj["fields"]!["b"]![0]!.GetValue<bool>());
    }

    [TestMethod]
    public void FailedMatch_CarriesOriginalmsgAndUnparsedData()
    {
        var ctx = Load("rule=:hello %w:word%");
        var r = ctx.Parse("goodbye world", out ParseResult result);

        Assert.AreNotEqual(0, r);
        Assert.IsFalse(result.Matched);
        Assert.IsTrue(result.TryGetRawText("originalmsg", out var orig));
        Assert.AreEqual("goodbye world", orig.ToString());
        Assert.IsTrue(result.Contains("unparsed-data"));
    }

    [TestMethod]
    public void Parse_FlatResult_ExposesFieldsWithoutMaterializing()
    {
        var ctx = Load("rule=:hello %first:word% %second:word%");
        var r = ctx.Parse("hello foo bar", out ParseResult result);

        Assert.AreEqual(0, r);
        Assert.AreEqual(0, result.Status);
        Assert.IsTrue(result.Matched);
        Assert.AreEqual(2, result.Count);
        /* commit order is deepest-first (reverse rule order) */
        Assert.AreEqual("second", result.GetName(0));
        Assert.AreEqual("first", result.GetName(1));
        Assert.IsTrue(result.Contains("first"));
        Assert.IsFalse(result.Contains("missing"));
        Assert.AreEqual("foo", result.GetValue("first")!.GetValue<string>());
        Assert.IsNull(result.GetValue("missing"));
    }

    [TestMethod]
    public void ParseToString_MatchesLegacyJsonObjectPath()
    {
        var ctx = Load("""
            rule=:a %w:word% n=%n:number% rest=%r:rest%
            """, ParseOptions.AddOriginalMessage);

        foreach (var message in new[] { "a x-é\"q\" n=12 rest=tail \\ end", "no match  here" })
        {
            var r1 = ctx.ParseToString(message, out var json);
            ctx.Parse(message, out JsonObject obj);
            Assert.AreEqual(JsonText.ToCompactString(obj), json);
            Assert.AreEqual(r1 == 0, !json.Contains("unparsed-data"));
        }
    }

    [TestMethod]
    public void Serialization_UsesMaterializedSnapshotAfterMutation()
    {
        var ctx = Load("rule=:hello %first:word% %second:word%");
        ctx.Parse("hello foo bar", out ParseResult result);

        var obj = result.ToJsonObject();
        obj["first"] = "modified";

        Assert.AreEqual("modified", result.GetValue("first")!.GetValue<string>());
        Assert.AreEqual("{\"second\":\"bar\",\"first\":\"modified\"}", result.ToJsonString());

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, JsonText.CompactWriterOptions))
        {
            result.WriteTo(writer);
        }

        Assert.AreEqual(result.ToJsonString(), Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    [TestMethod]
    public void ToJsonObject_IsCachedAndMatchesJsonObjectOverload()
    {
        var ctx = Load("rule=:hello %first:word% %second:word%");
        ctx.Parse("hello foo bar", out ParseResult result);
        ctx.Parse("hello foo bar", out JsonObject direct);

        var obj1 = result.ToJsonObject();
        var obj2 = result.ToJsonObject();
        Assert.AreSame(obj1, obj2, "ToJsonObject must be cached");
        Assert.IsTrue(JsonNode.DeepEquals(direct, obj1));
        /* GetValue reads from the cached object once materialized */
        Assert.AreEqual("foo", result.GetValue("first")!.GetValue<string>());
    }

    [TestMethod]
    public void ToJsonString_MatchesJsonObjectSerialization()
    {
        /* escaping parity between the span-direct writer and the JsonObject
         * path, over quotes, backslashes, non-ASCII and control characters */
        var ctx = Load("rule=:msg %rest:rest%");
        var payloads = new[] {
            "plain text",
            "with \"quotes\" and \\backslash\\",
            "unicode üß€ 中文",
            "control \t tab and  char",
            "<html> & 'entities'",
        };
        foreach (var payload in payloads)
        {
            ctx.Parse($"msg {payload}", out ParseResult result);
            Assert.AreEqual(
                JsonText.ToCompactString(result.ToJsonObject()),
                result.ToJsonString(),
                $"serialization mismatch for payload: {payload}");
        }
    }

    [TestMethod]
    public void TryGetRawText_FalseForConvertedValues()
    {
        var ctx = Load("""rule=:n %num:number{"format":"number"}%""");
        ctx.Parse("n 42", out ParseResult result);

        Assert.IsFalse(result.TryGetRawText("num", out _), "number-formatted field is not a raw slice");
        Assert.AreEqual(42, result.GetValue("num")!.GetValue<long>());
    }

    [TestMethod]
    public void TryGetRawText_ReturnsSliceOfInputMessage()
    {
        var ctx = Load("rule=:hello %first:word% %second:word%");
        const string message = "hello foo bar";
        ctx.Parse(message, out ParseResult result);

        Assert.IsTrue(result.TryGetRawText("first", out var text));
        Assert.AreEqual("foo", text.ToString());
        /* the memory is a genuine slice of the input, not a copy */
        Assert.IsTrue(System.Runtime.InteropServices.MemoryMarshal.TryGetString(text, out var source, out var start, out var length));
        Assert.AreSame(message, source);
        Assert.AreEqual(6, start);
        Assert.AreEqual(3, length);

        Assert.IsFalse(result.TryGetRawText("missing", out _));
    }

    [TestMethod]
    public void WriteTo_ProducesSameBytesAsToJsonString()
    {
        var ctx = Load("rule=:hello %first:word% %second:word%");
        ctx.Parse("hello foo bar", out ParseResult result);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, JsonText.CompactWriterOptions))
        {
            result.WriteTo(writer);
        }

        Assert.AreEqual(result.ToJsonString(), Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private static ParseContext Load(string rulebase, ParseOptions options = ParseOptions.None)
    {
        var ctx = new ParseContext { Options = options };
        Assert.AreEqual(0, ctx.LoadSamplesFromString(rulebase));
        return ctx;
    }
}
