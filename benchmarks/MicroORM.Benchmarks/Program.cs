// dotnet run -c Release --project benchmarks/MicroORM.Benchmarks -- --filter "*"

using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
