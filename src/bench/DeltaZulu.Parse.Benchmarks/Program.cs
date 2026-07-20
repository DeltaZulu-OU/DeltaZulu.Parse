using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(DeltaZulu.Parse.Benchmarks.ParsingBenchmarks).Assembly).Run(args);
