using LogCluster.Cli;

namespace DeltaZulu.Normalize.Tests;

[TestClass]
public class LogClusterMinerTests
{
    [TestMethod]
    public void InternalMultiwordGaps_AreRenderedAsUnresolvedSketchesNotRestParsers()
    {
        var options = LogClusterOptions.Parse(["--min-support", "2"]);
        var records = new[] {
            new LogRecord(1, "Interface Ethernet 1 down at node node1", "test"),
            new LogRecord(2, "Interface Ethernet 1 2 down at node node2", "test"),
        };

        var result = new LogClusterMiner(options).Mine(records);
        var candidate = result.Candidates.Single(c => c.LogClusterPattern.StartsWith("Interface", StringComparison.Ordinal));

        Assert.IsFalse(candidate.IsExecutableRule);
        Assert.Contains("/* unresolved gap:", candidate.LiblognormRule);
        Assert.DoesNotContain("%field1:rest% down at node", candidate.LiblognormRule);
        Assert.IsNotEmpty(candidate.RuleWarnings);
    }

    [TestMethod]
    public void Mine_ThrowsWhenRecordCountExceedsMaxRecords()
    {
        var options = LogClusterOptions.Parse(["--max-records", "2"]);
        var records = new[] {
            new LogRecord(1, "line one", "test"),
            new LogRecord(2, "line two", "test"),
            new LogRecord(3, "line three", "test"),
        };

        Assert.ThrowsExactly<LogClusterInputTooLargeException>(() => new LogClusterMiner(options).Mine(records));
    }

    [TestMethod]
    public void Mine_ThrowsWhenInputBytesExceedMaxInputBytes()
    {
        var options = LogClusterOptions.Parse(["--max-input-bytes", "10"]);
        var records = new[] {
            new LogRecord(1, "this line is definitely over ten bytes", "test"),
        };

        Assert.ThrowsExactly<LogClusterInputTooLargeException>(() => new LogClusterMiner(options).Mine(records));
    }
}