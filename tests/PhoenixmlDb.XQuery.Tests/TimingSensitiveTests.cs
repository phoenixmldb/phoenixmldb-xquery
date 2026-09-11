using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Tests that measure time — scaling ratios and cancellation latency — run in this collection,
/// alone. Run in parallel with the rest of the suite on a 2-core CI runner, a ratio taken at
/// small sizes measured the neighbours: missed numeric lookups scaled 13.9x for 4x the input on
/// net10.0 and passed on net8.0 in the same run, while the same code measured linear locally
/// (196/265/502/910 ms for 4k/16k/64k/256k, startup included).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515", Justification = "xunit discovers collection definitions by reflection; it must be public.")]
public sealed class TimingSensitiveTests
{
    public const string Name = "Timing-sensitive";
}
