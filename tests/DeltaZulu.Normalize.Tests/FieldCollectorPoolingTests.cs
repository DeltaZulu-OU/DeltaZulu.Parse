using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeltaZulu.Normalize.Tests;

/// <summary>
/// The classic Normalize(out JsonObject) overload rents its scratch
/// FieldCollector's backing array from ArrayPool&lt;Entry&gt;.Shared instead of
/// allocating it fresh every call, since the collector is discarded the
/// instant it is materialized. These pin the correctness properties that
/// make reusing that array across calls (and across threads) safe: results
/// must not leak between calls, growth past the pooled capacity must not
/// corrupt or double-return the array, and concurrent callers must not
/// observe each other's in-flight state.
/// </summary>
[TestClass]
public class FieldCollectorPoolingTests
{
    [TestMethod]
    public void RepeatedCalls_DoNotLeakFieldsAcrossPooledArrayReuse()
    {
        var ctx = new LogNormContext();
        Assert.AreEqual(0, ctx.LoadSamplesFromString("rule=:a %f:word%"));

        for (var i = 0; i < 50; i++)
        {
            var msg = $"a value{i}";
            Assert.AreEqual(0, ctx.Normalize(msg, out JsonObject j));
            Assert.AreEqual(1, j.Count, $"iteration {i}: stale fields from a previous call leaked in: {j.ToJsonString()}");
            Assert.AreEqual($"value{i}", j["f"]!.GetValue<string>());
        }
    }

    [TestMethod]
    public void MoreFieldsThanPooledCapacity_GrowsAndReturnsCorrectResult()
    {
        /* 6 fields exceeds the scratch collector's initial pooled capacity
         * (4), forcing Grow() to return the pooled array mid-flight and
         * switch to a plain heap array; the result must still be complete
         * and correct, and the subsequent ReturnScratch() must not attempt
         * to return the (already-returned) original array again. */
        var ctx = new LogNormContext();
        Assert.AreEqual(0, ctx.LoadSamplesFromString(
            "rule=:a %f1:word% %f2:word% %f3:word% %f4:word% %f5:word% %f6:word%"));

        Assert.AreEqual(0, ctx.Normalize("a 1 2 3 4 5 6", out JsonObject j));
        for (var i = 1; i <= 6; i++)
        {
            Assert.AreEqual(i.ToString(), j[$"f{i}"]!.GetValue<string>());
        }
    }

    [TestMethod]
    public void ConcurrentNormalizeCalls_DoNotCorruptEachOthersResults()
    {
        var ctx = new LogNormContext();
        Assert.AreEqual(0, ctx.LoadSamplesFromString("rule=:msg %n:number% payload %f:rest%"));

        var failures = new ConcurrentQueue<string>();
        var tasks = Enumerable.Range(0, Environment.ProcessorCount * 2).Select(t => Task.Run(() => {
            for (var i = 0; i < 500; i++)
            {
                var msg = $"msg {t} payload thread{t}-iter{i}";
                if (ctx.Normalize(msg, out JsonObject j) != 0
                    || j["n"]?.GetValue<string>() != t.ToString()
                    || j["f"]?.GetValue<string>() != $"thread{t}-iter{i}")
                {
                    failures.Enqueue($"thread {t} iter {i}: got {j.ToJsonString()}");
                    return;
                }
            }
        })).ToArray();

        Task.WaitAll(tasks, TimeSpan.FromSeconds(30));
        Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
    }

    [TestMethod]
    public void FailedMatch_AlsoRoundTripsThePooledArrayCorrectly()
    {
        /* a non-match still commits exactly two fields (originalmsg,
         * unparsed-data) via AddUnparsedField, exercising the same
         * rent/return path as a successful match */
        var ctx = new LogNormContext();
        Assert.AreEqual(0, ctx.LoadSamplesFromString("rule=:hello %f:word%"));

        for (var i = 0; i < 20; i++)
        {
            var msg = $"goodbye {i}";
            Assert.AreNotEqual(0, ctx.Normalize(msg, out JsonObject j));
            Assert.AreEqual(2, j.Count);
            Assert.AreEqual(msg, j["originalmsg"]!.GetValue<string>());
        }
    }
}
