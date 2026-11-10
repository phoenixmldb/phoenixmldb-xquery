using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Tests that put files in, or change, the process working directory run in this collection,
/// alone: <see cref="System.Environment.CurrentDirectory"/> is process-wide, so a test that
/// changes it moves every relative path of a test that runs at the same time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515", Justification = "xunit discovers collection definitions by reflection; it must be public.")]
public sealed class WorkingDirectoryTests
{
    public const string Name = "Working directory";
}
