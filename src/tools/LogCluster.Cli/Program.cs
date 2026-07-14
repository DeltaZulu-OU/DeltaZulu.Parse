using System.Text.Json;

namespace LogCluster.Cli
{
    internal static class Program
    {
        private static readonly JsonSerializerOptions jsonOpts = new JsonSerializerOptions { WriteIndented = true };

        private static int Main(string[] args)
        {
            var options = LogClusterOptions.Parse(args);
            if (options.ShowHelp)
            {
                LogClusterOptions.PrintUsage();
                return 0;
            }
            if (options.Error is not null)
            {
                Console.Error.WriteLine($"error: {options.Error}");
                LogClusterOptions.PrintUsage();
                return 1;
            }

            // LogClusterMiner.Mine needs to re-enumerate its record source multiple times (see the
            // ARCHITECTURE NOTE on LogClusterMiner): once per mining pass. Calling the local
            // `ReadRecords` function fresh for each pass gives it a brand-new iterator that
            // re-opens files from disk, so it never needs to buffer the corpus itself. Stdin is
            // the one source that cannot be re-read (a console/pipe stream is single-use), so it
            // is spooled to a temp file up front and then replayed from disk like any other file
            // input — this is the same limitation logcluster.pl's own stdin handling has (its `-`
            // dup of STDIN cannot be re-read across passes either), just made explicit instead of
            // silently returning zero records on the 2nd/3rd pass.
            string? stdinSpoolPath = null;
            try
            {
                Func<IEnumerable<LogRecord>> recordSource;
                if (options.Message is null && options.Inputs.Count == 0)
                {
                    stdinSpoolPath = SpoolStdin();
                    recordSource = () => ReadFile(stdinSpoolPath, new SequenceCounter(), options.SkipEmpty, "stdin");
                }
                else
                {
                    recordSource = () => ReadRecords(options);
                }

                var result = new LogClusterMiner(options).Mine(recordSource);
                if (result.RecordCount == 0)
                {
                    Console.Error.WriteLine("error: no input messages were provided");
                    return 1;
                }
                if (options.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(result.Candidates, jsonOpts));
                }
                else
                {
                    PrintText(result, options);
                }

                return 0;
            }
            catch (LogClusterInputTooLargeException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 1;
            }
            finally
            {
                if (stdinSpoolPath is not null)
                {
                    File.Delete(stdinSpoolPath);
                }
            }

            static string SpoolStdin()
            {
                var path = Path.Combine(Path.GetTempPath(), $"logcluster-stdin-{Guid.NewGuid():N}.tmp");
                using var writer = new StreamWriter(path);
                string? line;
                while ((line = Console.In.ReadLine()) is not null)
                {
                    writer.WriteLine(line);
                }
                return path;
            }

            static IEnumerable<LogRecord> ReadRecords(LogClusterOptions options)
            {
                var sequence = new SequenceCounter();
                if (options.Message is not null)
                {
                    yield return new LogRecord(sequence.Next(), options.Message, "argument");
                    yield break;
                }

                foreach (var input in options.Inputs)
                {
                    if (Directory.Exists(input))
                    {
                        foreach (var file in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                        {
                            foreach (var record in ReadFile(file, sequence, options.SkipEmpty, file))
                            {
                                yield return record;
                            }
                        }
                    }
                    else
                    {
                        foreach (var record in ReadFile(input, sequence, options.SkipEmpty, input))
                        {
                            yield return record;
                        }
                    }
                }
            }

            static IEnumerable<LogRecord> ReadFile(string path, SequenceCounter sequence, bool skipEmpty, string sourceLabel)
            {
                using var reader = File.OpenText(path);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line.Length != 0 || !skipEmpty)
                    {
                        yield return new LogRecord(sequence.Next(), line, sourceLabel);
                    }
                }
            }

            static void PrintText(MiningResult result, LogClusterOptions options)
            {
                Console.WriteLine($"LogCluster.NET candidates: {result.Candidates.Count} (records: {result.RecordCount}, minimum support: {options.MinSupport})");
                Console.WriteLine();

                foreach (var candidate in result.Candidates)
                {
                    Console.WriteLine($"Score {candidate.Score.Total:F1}  Support {candidate.Support}  Specificity {candidate.Specificity:F2}");
                    Console.WriteLine($"  LogCluster: {candidate.LogClusterPattern}");
                    Console.WriteLine($"  Rule:       {candidate.LiblognormRule}");
                    Console.WriteLine($"  Score parts support={candidate.Score.Support:F1}, anchors={candidate.Score.AnchorQuality:F1}, gaps={candidate.Score.GapConsistency:F1}, specificity={candidate.Score.PatternSpecificity:F1}");
                    if (options.Verbose)
                    {
                        for (var i = 0; i < candidate.Gaps.Count; i++)
                        {
                            var gap = candidate.Gaps[i];
                            var parser = gap.SuggestedParser ?? LiblognormMotifs.Rest;
                            Console.WriteLine($"  Gap {i + 1}: words {gap.MinWords}-{gap.MaxWords}, observations {gap.Observations}, parser {parser} ({gap.ParserConfidence:P0})");
                            if (gap.Samples.Count > 0)
                            {
                                Console.WriteLine($"    samples: {string.Join(", ", gap.Samples)}");
                            }
                        }
                    }
                    Console.WriteLine();
                }
            }
        }
    }

    internal sealed class SequenceCounter
    {
        private long _value;

        public long Next() => ++_value;
    }
}