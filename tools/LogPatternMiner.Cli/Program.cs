using DeltaZulu.Normalize.PatternMining;

var inputs = new List<string>();
long? support = null;
double relativeSupport = 0.1;
double minimumScore = 0;
int? top = null;
var separator = @"\s+";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--support": support = long.Parse(RequireValue(args, ref i)); break;
        case "--relative-support": relativeSupport = double.Parse(RequireValue(args, ref i), System.Globalization.CultureInfo.InvariantCulture); break;
        case "--minimum-score": minimumScore = double.Parse(RequireValue(args, ref i), System.Globalization.CultureInfo.InvariantCulture); break;
        case "--top": top = int.Parse(RequireValue(args, ref i)); break;
        case "--separator": separator = RequireValue(args, ref i); break;
        case "-h":
        case "--help": PrintUsage(); return 0;
        default:
            if (args[i].StartsWith('-', StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"unknown option: {args[i]}");
                return 1;
            }
            inputs.Add(args[i]);
            break;
    }
}

if (inputs.Count == 0)
{
    Console.Error.WriteLine("error: at least one input file is required");
    PrintUsage();
    return 1;
}

var miner = new LogClusterMiner(new RegexLogTokenizer(separator));
var result = await miner.MineAsync(new FileLogSource(inputs), new MiningOptions
{
    Support = support,
    RelativeSupportPercent = relativeSupport,
    MinimumScore = minimumScore,
    Top = top
});

Console.Error.WriteLine($"records={result.TotalRecords} support={result.SupportThreshold} candidates={result.Suggestions.Count}");
foreach (var suggestion in result.Suggestions)
{
    var score = suggestion.Candidate.Score;
    Console.WriteLine($"[{score.Total:F2}] support={suggestion.Candidate.RawSupport}");
    Console.WriteLine(suggestion.LogClusterPattern);
    Console.WriteLine(suggestion.LiblognormSuggestion);
    Console.WriteLine($"  components: support={score.SupportStrength:F2} anchors={score.AnchorQuality:F2} gaps={score.GapConsistency:F2} specificity={score.Specificity:F2}");
    foreach (var example in suggestion.Candidate.Examples.Items)
        Console.WriteLine($"  example: {example}");
    Console.WriteLine();
}

return 0;

static string RequireValue(string[] values, ref int index)
{
    if (++index >= values.Length)
        throw new ArgumentException($"Missing value for {values[index - 1]}.");
    return values[index];
}

static void PrintUsage() => Console.Error.WriteLine("""
usage: logpatternminer [options] <file> [file...]

  --support <n>              absolute minimum support
  --relative-support <pct>   relative support percentage (default: 0.1)
  --minimum-score <0-100>    suppress lower-ranked suggestions
  --top <n>                  print only the first n suggestions
  --separator <regex>        token separator (default: whitespace)
""");
