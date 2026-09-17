using BenchmarkDotNet.Running;

// dotnet run -c Release --project benchmarks/GameOfLife.Benchmarks -- --filter '*'
// Results land in BenchmarkDotNet.Artifacts/results; the committed baselines live in benchmarks/results.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
