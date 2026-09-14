using FluentAssertions;
using System.Xml.Linq;
using Xunit;


namespace PhoenixmlDb.Conformance.Tests.XQuery;

/// <summary>
/// XQuery Test Suite (XQTS) conformance tests.
///
/// These tests run against the W3C XQuery Test Suite to verify
/// conformance with XQuery 3.1 and XPath 3.1 specifications.
///
/// Test Suite Sources:
/// - QT3 Tests (XQuery 3.1): https://github.com/w3c/qt3tests
/// - QT4 Tests (XQuery 4.0): https://github.com/qt4cg/qt4tests
///
/// To run these tests, clone the test suite repository to the TestData directory:
///   git clone https://github.com/w3c/qt3tests tests/PhoenixmlDb.Conformance.Tests/TestData/qt3tests
/// </summary>
[Trait("Category", "Conformance")]
[Trait("Suite", "XQTS")]
public class XqtsConformanceTests : IClassFixture<XqtsTestFixture>
{
    private readonly XqtsTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public XqtsConformanceTests(XqtsTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task Xqts_ShouldPassBasicArithmeticTests()
    {
        // Basic arithmetic tests to verify engine is working
        var testCases = new[]
        {
            ("1 + 1", "2"),
            ("10 - 3", "7"),
            ("5 * 4", "20"),
            ("20 div 4", "5"),
            ("17 mod 5", "2"),
        };

        foreach (var (query, expected) in testCases)
        {
            var result = await _fixture.RunQueryAsync(query);
            result?.ToString().Should().Be(expected, $"Query: {query}");
        }
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task Xqts_ShouldPassBasicComparisonTests()
    {
        var testCases = new[]
        {
            ("1 < 2", "true"),
            ("5 > 3", "true"),
            ("3 = 3", "true"),
            ("4 != 5", "true"),
            ("2 <= 2", "true"),
            ("3 >= 3", "true"),
        };

        foreach (var (query, expected) in testCases)
        {
            var result = await _fixture.RunQueryAsync(query);
            result?.ToString()?.ToLower().Should().Be(expected, $"Query: {query}");
        }
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task Xqts_ShouldPassBasicStringFunctionTests()
    {
        var testCases = new[]
        {
            ("string-length('hello')", "5"),
            ("concat('hello', ' ', 'world')", "hello world"),
            ("upper-case('hello')", "HELLO"),
            ("lower-case('HELLO')", "hello"),
            ("substring('hello', 2, 3)", "ell"),
            ("contains('hello', 'ell')", "true"),
            ("starts-with('hello', 'hel')", "true"),
            ("ends-with('hello', 'llo')", "true"),
        };

        foreach (var (query, expected) in testCases)
        {
            var result = await _fixture.RunQueryAsync(query);
            var actual = result?.ToString()?.ToLower() ?? "";
            expected.ToLower().Should().Be(actual, $"Query: {query}");
        }
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task Xqts_ShouldPassBasicNumericFunctionTests()
    {
        var testCases = new[]
        {
            ("abs(-5)", "5"),
            ("floor(3.7)", "3"),
            ("ceiling(3.2)", "4"),
            ("round(3.5)", "4"),
            ("sum((1, 2, 3, 4, 5))", "15"),
            ("count((1, 2, 3))", "3"),
            ("min((3, 1, 2))", "1"),
            ("max((1, 3, 2))", "3"),
        };

        foreach (var (query, expected) in testCases)
        {
            var result = await _fixture.RunQueryAsync(query);
            result?.ToString().Should().Be(expected, $"Query: {query}");
        }
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task Xqts_ShouldPassBasicSequenceFunctionTests()
    {
        var testCases = new[]
        {
            ("empty(())", "true"),
            ("exists((1, 2, 3))", "true"),
            ("head((1, 2, 3))", "1"),
            ("reverse((1, 2, 3))", "3 2 1"),
            ("distinct-values((1, 2, 2, 3, 3, 3))", "1 2 3"),
        };

        foreach (var (query, expected) in testCases)
        {
            var result = await _fixture.RunQueryAsync(query);
            var actual = result?.ToString() ?? "";
            // Normalize sequence output
            if (result is IEnumerable<object> seq)
            {
                actual = string.Join(" ", seq);
            }
            actual.ToLower().Should().Be(expected.ToLower(), $"Query: {query}");
        }
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task Xqts_ShouldPassBasicFlworTests()
    {
        var query = @"
            for $x in (1, 2, 3)
            return $x * 2
        ";
        var result = await _fixture.RunQueryAsync(query);

        if (result is IEnumerable<object> seq)
        {
            var values = seq.Select(x => x?.ToString()).ToList();
            values.Should().BeEquivalentTo(new[] { "2", "4", "6" });
        }
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task Xqts_ShouldPassBasicLetClauseTests()
    {
        var query = @"
            let $x := 10
            let $y := 20
            return $x + $y
        ";
        var result = await _fixture.RunQueryAsync(query);
        result?.ToString().Should().Be("30");
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task Xqts_ShouldPassBasicWhereClauseTests()
    {
        var query = @"
            for $x in (1, 2, 3, 4, 5)
            where $x > 3
            return $x
        ";
        var result = await _fixture.RunQueryAsync(query);

        if (result is IEnumerable<object> seq)
        {
            var values = seq.Select(x => x?.ToString()).ToList();
            values.Should().BeEquivalentTo(new[] { "4", "5" });
        }
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task Xqts_ShouldPassBasicOrderByTests()
    {
        var query = @"
            for $x in (3, 1, 4, 1, 5)
            order by $x
            return $x
        ";
        var result = await _fixture.RunQueryAsync(query);

        if (result is IEnumerable<object> seq)
        {
            var values = seq.Select(x => x?.ToString()).ToList();
            values.Should().BeEquivalentTo(new[] { "1", "1", "3", "4", "5" });
        }
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task Xqts_ShouldPassBasicIfThenElseTests()
    {
        var testCases = new[]
        {
            ("if (true()) then 'yes' else 'no'", "yes"),
            ("if (false()) then 'yes' else 'no'", "no"),
            ("if (1 > 0) then 'positive' else 'negative'", "positive"),
        };

        foreach (var (query, expected) in testCases)
        {
            var result = await _fixture.RunQueryAsync(query);
            result?.ToString().Should().Be(expected, $"Query: {query}");
        }
    }

    /// <summary>
    /// Every test-set the QT3 catalog declares — 428 of them — one theory case each.
    /// </summary>
    /// <remarks>
    /// This used to be nine hard-coded <c>InlineData</c> rows, with the other 419 sets reachable
    /// only through a single whole-suite xunit test (since removed) that ran all
    /// 31,414 cases that prints nothing until it returns. That shape cost real time twice. It is
    /// why a per-chunk timeout was read as a hang and went uninvestigated for weeks (BUGS.md #33),
    /// and why a run that stopped after nine sets looked like a 39x slowdown, then like a wedge,
    /// before turning out to be one long test being killed mid-flight — a diagnosis that took
    /// three runs at different caps to reach and would have been obvious from a per-set listing.
    /// <para>
    /// Per-set cases give progress you can watch, a duration per set so a slow one is named
    /// rather than inferred, a partial result when the suite is cut short instead of nothing, and
    /// per-set counts the conformance gate can ratchet on exactly as it does for XSLT.
    /// </para>
    /// <para>
    /// Enumerated from the catalog rather than listed, so a suite update adds its sets here
    /// automatically instead of silently going unrun — the "corpus declares something the runner
    /// never reads" defect this project keeps finding (BUGS.md #41).
    /// </para>
    /// </remarks>
    public static TheoryData<string, string> AllTestSets()
    {
        var data = new TheoryData<string, string>();
        var catalog = Path.Combine(ConformanceSuites.Locate("qt3tests", "QT3_TEST_SUITE"), "catalog.xml");
        if (!File.Exists(catalog))
        {
            // No corpus: emit one placeholder so the theory is not empty (xunit fails an empty
            // MemberData). The test body then hits the same Assert.Skip as every other case.
            data.Add("none", "none");
            return data;
        }

        var doc = XDocument.Load(catalog);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        foreach (var ts in doc.Descendants(ns + "test-set"))
        {
            var name = ts.Attribute("name")?.Value;
            var file = ts.Attribute("file")?.Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(file)) continue;
            // Category is the catalog's own directory grouping (fn/, op/, prod/, misc/).
            var category = file.Contains('/', StringComparison.Ordinal)
                ? file[..file.IndexOf('/', StringComparison.Ordinal)]
                : "misc";
            data.Add(category, name);
        }
        return data;
    }

    [Theory]
    [Trait("Category", "Full")]
    [MemberData(nameof(AllTestSets))]
    public async Task Xqts_ShouldPassTestSet(string category, string testSetName)
    {
        if (!_fixture.IsTestDataAvailable)
        {
            _output.WriteLine("XQTS test data not available. Skipping test.");
            // Assert.Skip, not return: an early return is recorded as a PASS, so a missing
            // suite made this file report green having executed nothing.
            Assert.Skip("W3C QT3 suite not found. Set QT3_TEST_SUITE, or run scripts/fetch-conformance-suites.sh.");
        }

        // A FRESH runner per set. The runner accumulates state as it runs — loaded schemas,
        // registered document URIs, resource mappings, a document store, one engine — and a
        // shared one carried every set's leftovers into the next. The monolith ran in catalog
        // order, so that contamination was at least stable; as a theory the sets run in
        // xunit's order, which follows the assembly PATH, so the same commits scored
        // differently from two checkouts: method-html 40/64 from one, 36/64 from another,
        // and 49/64 alone. A set's result must be a property of the set. (BUGS.md #44, incident 12)
        var runner = _fixture.CreateRunner();
        var testCases = await runner.LoadTestSetByNameAsync(testSetName, TestContext.Current.CancellationToken);
        _output.WriteLine($"Running {testCases.Count} tests from {category}/{testSetName}");

        var passed = 0;
        var failed = 0;

        foreach (var testCase in testCases)
        {
            var result = await runner.RunTestAsync(testCase, TestContext.Current.CancellationToken);
            if (result.Passed)
            {
                passed++;
            }
            else
            {
                failed++;
                _output.WriteLine($"FAILED: {testCase.Name}");
                if (result.Error != null)
                {
                    _output.WriteLine($"  Error: {result.Error.Message}");
                }
                else if (failed <= 20) // Show details for first 20 assertion failures
                {
                    _output.WriteLine($"  Query: {testCase.Query[..Math.Min(120, testCase.Query.Length)]}");
                    _output.WriteLine($"  Actual: {result.ActualResult}");
                    _output.WriteLine($"  Expected: {string.Join(", ", testCase.Assertions.Select(a => $"{a.Type}={a.Value}"))}");
                }
            }
        }

        _output.WriteLine($"Results: {passed}/{testCases.Count} passed ({(double)passed / testCases.Count * 100:F1}%)");

        // assert-eq cases that passed ONLY because the legacy string comparison rescued them
        // after the engine said unequal. Each is a possible masked engine bug, so the count
        // belongs in the output rather than hidden. It used to be reported once, globally, by
        // the whole-suite test; with a fresh runner per set this is per-set, which attributes
        // it to somewhere rather than to everywhere. Printed only when non-zero so it reads as
        // a signal rather than noise.
        if (runner.EqStringFallbackRescues > 0)
        {
            _output.WriteLine(
                $"assert-eq string-compare rescues: {runner.EqStringFallbackRescues} "
                + "(each is an assert-eq that the engine judged unequal and a string comparison "
                + "rescued — a possible masked defect)");
        }

        // NOT `passed > 0`. That assertion was removed deliberately: it is a false RED on the
        // sets that legitimately score zero — five QT3 sets do — and a false GREEN everywhere
        // else, since one passing case in a set of two hundred satisfied it. It never gated
        // anything (BUGS.md #7). The real gate is the per-set ratchet in
        // scripts/conformance.sh, which fails the run if any set drops below what it reaches
        // every time.
        //
        // What IS asserted here is that the set RAN: every loaded case produced a verdict.
        // A runner that silently skipped cases is the defect this suite keeps finding — 419 of
        // 428 QT3 sets unreachable behind a hard-coded list, a MemberData placeholder yielding
        // one case instead of 428, a monolith killed mid-flight reporting the same nine sets at
        // three different caps. Every one of those looked green. Asserting the COUNT is what
        // tells a run that did nothing from a run that found nothing (BUGS.md #44).
        (passed + failed).Should().Be(testCases.Count,
            $"every case in {testSetName} must produce a verdict; a case that is neither passed nor "
            + "failed was silently dropped by the runner");
    }

}

/// <summary>
/// Fixture for XQTS tests.
/// </summary>
public sealed class XqtsTestFixture : IAsyncLifetime
{
    private readonly string _testDataPath;
    public XqtsTestRunner Runner { get; private set; } = null!;
    public bool IsTestDataAvailable { get; private set; }

    public XqtsTestFixture()
    {
        _testDataPath = ConformanceSuites.Locate("qt3tests", "QT3_TEST_SUITE");
    }

    public ValueTask InitializeAsync()
    {
        IsTestDataAvailable = File.Exists(Path.Combine(_testDataPath, "catalog.xml"));

        var config = new XqtsConfiguration
        {
            XQueryVersion = "3.1",
            SupportsHigherOrderFunctions = true,
            SupportsSchemaValidation = true
        };

        // Skip known unsupported tests
        config.SkipTests.Add("fn-transform"); // Requires XSLT support in fn:transform
        config.SkipTests.Add("fn-parse-xml-fragment"); // DTD handling

        _config = config;
        Runner = new XqtsTestRunner(_testDataPath, config);
        return ValueTask.CompletedTask;
    }

    private XqtsConfiguration _config = null!;

    /// <summary>
    /// A runner with no history. <see cref="Runner"/> is shared and accumulates state across
    /// everything it runs, so anything whose result must not depend on what ran before it —
    /// the per-set theory — takes one of these instead.
    /// </summary>
    public XqtsTestRunner CreateRunner() => new(_testDataPath, _config);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task<object?> RunQueryAsync(string query)
    {
        var testCase = new XqtsTestCase
        {
            Name = "inline",
            TestSet = "inline",
            Query = query,
            Assertions = new List<XqtsAssertion>()
        };

        var result = await Runner.RunTestAsync(testCase, TestContext.Current.CancellationToken);
        if (result.Error != null)
        {
            throw result.Error;
        }
        return result.ActualResult;
    }

    public async Task<IReadOnlyList<XqtsTestCase>> LoadTestSetAsync(string category, string testSetName)
    {
        // Look up the test-set by name from the master catalog.
        // The catalog maps names like "fn-abs" to files like "fn/abs.xml".
        return await Runner.LoadTestSetByNameAsync(testSetName);
    }

    public async Task<IReadOnlyList<XqtsTestCase>> LoadAllTestsAsync()
    {
        return await Runner.LoadTestCasesAsync("catalog.xml");
    }
}
