using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using PhoenixmlDb.XQuery.Benchmarks;

// In-process, on the runtime of this build: run the net8.0 and net10.0 builds in turn. The
// arguments go to BenchmarkDotNet (--filter, --artifacts ...). Results are per element: divide
// the mean by the Elements column, or read the per-operation figures BenchmarkDotNet reports.
var config = ManualConfig.CreateMinimumViable()
    .AddLogger(ConsoleLogger.Default)
    .AddDiagnoser(BenchmarkDotNet.Diagnosers.MemoryDiagnoser.Default)
    .AddExporter(BenchmarkDotNet.Exporters.Csv.CsvExporter.Default)
    .AddExporter(BenchmarkDotNet.Exporters.MarkdownExporter.GitHub)
    .AddJob(Job.Default
        .WithToolchain(new BenchmarkDotNet.Toolchains.InProcess.Emit.InProcessEmitToolchain(TimeSpan.FromMinutes(30), logOutput: true))
        .WithStrategy(BenchmarkDotNet.Engines.RunStrategy.Monitoring)
        .WithLaunchCount(1).WithWarmupCount(2).WithIterationCount(7));
BenchmarkSwitcher.FromTypes([typeof(XPathPerNodeBenchmarks)]).Run(args, config);
return 0;
