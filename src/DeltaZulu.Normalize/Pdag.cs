using System.Text.Json.Nodes;

namespace DeltaZulu.Normalize;

/// <summary>
/// A node of the parse DAG (PDAG).
///
/// The rulebase is compiled into a rooted directed acyclic graph. Each node
/// holds an ordered list of outgoing edges (<see cref="ParserInstance"/>);
/// each edge is a motif parser that, when it matches a prefix of the
/// remaining input, leads to the next node. A terminal node marks the end of
/// at least one rule. User-defined types are separate, disconnected PDAG
/// components referenced by name.
/// </summary>
public sealed class Pdag
{
    internal Pdag(LogNormContext ctx)
    {
        Ctx = ctx;
        ctx.NodeCount++;
    }

    internal LogNormContext Ctx { get; }

    /// <summary>Outgoing edges, evaluated in order (sorted by priority after optimization).</summary>
    internal List<ParserInstance> Parsers { get; } = new();

    /// <summary>True when at least one rule ends at this node.</summary>
    public bool IsTerminal { get; internal set; }

    /// <summary>Tags assigned to messages that match a rule ending here.</summary>
    internal JsonArray? Tags { get; set; }

    /// <summary>Number of edges pointing at this node (needed by the literal-path optimizer).</summary>
    internal int RefCount = 1;

    /// <summary>Human-readable rulebase identifier of this node (set during optimization).</summary>
    public string? RulebaseId { get; internal set; }

    /// <summary>Rulebase file that defined the rule terminating here (for rule-location metadata).</summary>
    internal string? RulebaseFile;
    internal int RulebaseLineNumber;

    /// <summary>Usage statistics, maintained during normalization.</summary>
    public int StatsCalled { get; internal set; }
    public int StatsBacktracked { get; internal set; }

    internal bool IsLeaf => Parsers.Count == 0;
}

/// <summary>
/// One outgoing edge of a PDAG node: a concrete motif parser instance
/// together with the node to branch to when it matches.
/// </summary>
internal sealed class ParserInstance
{
    /// <summary>Parser ID: index into <see cref="ParserTable.Parsers"/>, or <see cref="ParserTable.CustomTypeId"/>.</summary>
    public required byte PrsId { get; init; }

    /// <summary>Node to branch to when this parser succeeds.</summary>
    public Pdag Node = null!;

    /// <summary>Parser-specific configuration data built by the parser's construct function.</summary>
    public object? ParserData;

    /// <summary>Index of the user-defined type, when <see cref="PrsId"/> is the custom-type ID.</summary>
    public int CustomTypeIndex = -1;

    /// <summary>Combined priority: user-assigned priority in the upper bits,
    /// parser-specific priority in the low 8 bits. Lower sorts first.</summary>
    public int Priority;

    /// <summary>Field name, or null when the value is matched but not extracted ("-").</summary>
    public string? Name;

    /// <summary>Canonical config text, used to detect identical parsers for node merging.</summary>
    public required string Conf { get; init; }
}

/// <summary>A named, disconnected PDAG component modelling a user-defined type.</summary>
internal sealed class TypePdag
{
    public required string Name { get; init; }
    public required Pdag Dag { get; init; }
}
