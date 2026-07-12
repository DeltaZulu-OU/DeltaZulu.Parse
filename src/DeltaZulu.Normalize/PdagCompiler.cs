using System.Text;
using DeltaZulu.Normalize.Parsers;

namespace DeltaZulu.Normalize;

/// <summary>
/// Compiles the mutable builder graph (<see cref="Pdag"/>/<see cref="ParserInstance"/>)
/// into an immutable <see cref="CompiledPdag"/> snapshot. The optimization
/// passes that pdag.c applies in place (priority sort, literal path
/// compaction) happen here, during flattening, so the builder graph is never
/// mutated and can be compiled again after further rulebase loads — which is
/// what makes hot reload possible.
/// </summary>
internal static class PdagCompiler
{
    public static CompiledPdag Compile(LogNormContext ctx)
    {
        var state = new State();

        /* the main component first, so its root lands at index 0 */
        state.CompileNode(ctx.Root, optimize: true);

        int[] typeRoots = new int[ctx.TypePdags.Count];
        for (int i = 0; i < ctx.TypePdags.Count; ++i)
            typeRoots[i] = state.CompileNode(ctx.TypePdags[i].Dag, optimize: true);

        var snap = new CompiledPdag
        {
            Nodes = state.BuildNodes(out CompiledEdge[] edges),
            Edges = edges,
            Terminals = state.Terminals.ToArray(),
            TypeRoots = typeRoots,
        };
        if ((ctx.Options & LogNormOptions.CollectStats) != 0)
        {
            snap.StatsCalled = new int[snap.Nodes.Length];
            snap.StatsBacktracked = new int[snap.Nodes.Length];
        }
        return snap;
    }

    private sealed class State
    {
        private sealed class TempNode
        {
            public readonly List<CompiledEdge> Edges = new();
            public int TerminalIdx = -1;
            public int RefCount;
        }

        private readonly Dictionary<Pdag, int> _map = new();
        private readonly List<TempNode> _nodes = new();
        public readonly List<TerminalInfo> Terminals = new();

        /// <summary>
        /// Compile one builder node (and, recursively, everything reachable
        /// from it) into the arena, returning its node index. Shared builder
        /// nodes map to a single compiled node, preserving the DAG shape.
        ///
        /// <paramref name="optimize"/> selects the passes ln_pdagOptimize runs
        /// on the main and type components: priority-sorting each node's edges
        /// and compacting unnamed literal chains. "repeat" sub-components are
        /// compiled verbatim (the C engine never optimizes them, and sorting
        /// would change their evaluation order).
        /// </summary>
        public int CompileNode(Pdag dag, bool optimize)
        {
            if (_map.TryGetValue(dag, out int idx))
                return idx;
            idx = _nodes.Count;
            _map[dag] = idx;
            var tmp = new TempNode { RefCount = dag.RefCount };
            _nodes.Add(tmp);

            if (dag.IsTerminal)
            {
                tmp.TerminalIdx = Terminals.Count;
                Terminals.Add(new TerminalInfo
                {
                    Tags = dag.Tags,
                    RulebaseFile = dag.RulebaseFile,
                    RulebaseLineNumber = dag.RulebaseLineNumber,
                });
            }

            /* stable sort keeps rule-definition order for equal priorities */
            IEnumerable<ParserInstance> parsers = optimize && dag.Parsers.Count > 1
                ? dag.Parsers.OrderBy(p => p.Priority)
                : dag.Parsers;

            foreach (ParserInstance prs in parsers)
            {
                object? data = prs.ParserData;
                Pdag target = prs.Node;

                if (optimize && prs.PrsId == ParserTable.LiteralId && prs.Name == null)
                {
                    /* literal path compaction: merge a chain of single-char
                     * literal edges into one multi-char literal edge. Only
                     * chains through unshared, non-terminal, single-edge
                     * nodes qualify (same conditions as pdag.c's
                     * optLitPathCompact); skipped nodes are simply never
                     * compiled. The builder's literal data is not mutated —
                     * a merged edge gets fresh data. */
                    string lit = ((LiteralParser.Data)data!).Lit;
                    bool merged = false;
                    while (!target.IsTerminal
                           && target.RefCount == 1
                           && target.Parsers.Count == 1
                           && target.Parsers[0].PrsId == ParserTable.LiteralId
                           && target.Parsers[0].Name == null
                           && target.Parsers[0].Node.RefCount == 1)
                    {
                        lit += ((LiteralParser.Data)target.Parsers[0].ParserData!).Lit;
                        target = target.Parsers[0].Node;
                        merged = true;
                    }
                    if (merged)
                        data = new LiteralParser.Data { Lit = lit };
                }
                else if (prs.PrsId == ParserTable.RepeatId)
                {
                    var rd = (RepeatParser.Data)data!;
                    data = new RepeatParser.CompiledData
                    {
                        ParserRoot = CompileNode(rd.Parser, optimize: false),
                        WhileRoot = CompileNode(rd.WhileCond, optimize: false),
                        PermitMismatchInParser = rd.PermitMismatchInParser,
                        FailOnDuplicate = rd.FailOnDuplicate,
                    };
                }

                int targetIdx = CompileNode(target, optimize);
                char firstChar = '\0';
                if (prs.PrsId == ParserTable.LiteralId)
                {
                    string lit = ((LiteralParser.Data)data!).Lit;
                    if (lit.Length > 0)
                        firstChar = lit[0];
                }
                tmp.Edges.Add(new CompiledEdge(prs.PrsId, firstChar, targetIdx,
                    prs.CustomTypeIndex, data, prs.Name));
            }
            return idx;
        }

        /// <summary>Flatten the per-node edge lists into the final contiguous arrays.</summary>
        public CompiledNode[] BuildNodes(out CompiledEdge[] edges)
        {
            var nodes = new CompiledNode[_nodes.Count];
            int edgeCount = 0;
            foreach (TempNode t in _nodes)
                edgeCount += t.Edges.Count;
            edges = new CompiledEdge[edgeCount];

            int offset = 0;
            for (int i = 0; i < _nodes.Count; ++i)
            {
                TempNode t = _nodes[i];
                t.Edges.CopyTo(edges, offset);
                nodes[i] = new CompiledNode(offset, t.Edges.Count, t.TerminalIdx, t.RefCount);
                offset += t.Edges.Count;
            }
            return nodes;
        }
    }

    /* ---------- DOT graph generation (debug aid) ---------- */

    /// <summary>
    /// GraphViz DOT description of the snapshot's main component (port of
    /// ln_genDotPDAGGraph, previously generated from the builder graph).
    /// </summary>
    public static string GenerateDot(CompiledPdag snap)
    {
        var sb = new StringBuilder();
        var visited = new HashSet<int>();
        sb.Append("digraph pdag {\n");
        GenerateDotRec(snap, snap.RootNode, sb, visited);
        sb.Append("}\n");
        return sb.ToString();
    }

    private static void GenerateDotRec(CompiledPdag snap, int nodeIdx, StringBuilder sb, HashSet<int> visited)
    {
        if (!visited.Add(nodeIdx))
            return;
        CompiledNode node = snap.Nodes[nodeIdx];
        sb.Append($"l{nodeIdx} [ label=\"{node.RefCount}\"");
        if (node.IsTerminal)
            sb.Append(" style=\"bold\"");
        sb.Append("]\n");
        for (int i = node.EdgeStart; i < node.EdgeStart + node.EdgeCount; ++i)
        {
            CompiledEdge edge = snap.Edges[i];
            sb.Append($"l{nodeIdx} -> l{edge.TargetNode} [label=\"");
            sb.Append(ParserTable.IdToName(edge.PrsId));
            sb.Append(':');
            if (edge.PrsId == ParserTable.LiteralId)
            {
                foreach (char c in LiteralParser.DataForDisplay(edge.Data!))
                {
                    if (c != '\\' && c != '"')
                        sb.Append(c);
                }
            }
            sb.Append("\" style=\"dotted\"]\n");
            GenerateDotRec(snap, edge.TargetNode, sb, visited);
        }
    }
}
