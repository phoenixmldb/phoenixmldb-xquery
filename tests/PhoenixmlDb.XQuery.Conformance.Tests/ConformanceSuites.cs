namespace PhoenixmlDb.Conformance.Tests;

/// <summary>
/// Locates a W3C conformance suite on disk.
/// </summary>
/// <remarks>
/// <para>
/// The suites are large — the XSLT 3.0 suite is roughly 470 MB across ~30,000 files — so they
/// are never committed and never copied into the test output. A <c>CopyToOutputDirectory</c>
/// glob over one of them adds about seven minutes to every build of this project.
/// </para>
/// <para>
/// They are fetched instead by <c>scripts/fetch-conformance-suites.sh</c>, which pins an exact
/// suite revision. The pin is the point: a conformance percentage is not reproducible, and not
/// comparable across runs, unless the suite revision it was measured against is known.
/// </para>
/// <para>
/// The historical layout — a directory beside the test assembly, populated by a symlink — is
/// still honoured as a fallback so an existing local checkout keeps working. That layout is
/// what tied conformance runs to one machine; prefer the environment variable.
/// </para>
/// </remarks>
internal static class ConformanceSuites
{
    /// <summary>
    /// Resolves <paramref name="suiteName"/> from <paramref name="environmentVariable"/> when it
    /// is set, else from the legacy <c>TestData/&lt;suiteName&gt;</c> directory beside the assembly.
    /// Returns a path that may not exist; callers report absence as a skip.
    /// </summary>
    public static string Locate(string suiteName, string environmentVariable)
    {
        var configured = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured.Trim());

        var assemblyDir = Path.GetDirectoryName(typeof(ConformanceSuites).Assembly.Location)!;
        return Path.Combine(assemblyDir, "TestData", suiteName);
    }
}
