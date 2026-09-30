using FluentAssertions;
using System.Xml.Linq;
using Xunit;

namespace PhoenixmlDb.Conformance.Tests.XQuery;

/// <summary>
/// Fails when the QT3 catalog uses an element or attribute that <see cref="HarnessVocabulary"/>
/// does not account for, so a catalog feature the harness does not know about is reported
/// rather than silently mis-scoring the cases that use it.
/// </summary>
public class HarnessVocabularyTests
{
    private static readonly XNamespace Fots = "http://www.w3.org/2010/09/qt-fots-catalog";

    [Fact]
    public void Every_catalog_element_and_attribute_is_accounted_for()
    {
        var suite = ConformanceSuites.Locate("qt3tests", "QT3_TEST_SUITE");
        var catalog = Path.Combine(suite, "catalog.xml");
        if (!File.Exists(catalog))
            Assert.Skip("W3C QT3 suite not found. Set QT3_TEST_SUITE, or run scripts/fetch-conformance-suites.sh.");

        var used = new SortedSet<string>(StringComparer.Ordinal);
        var files = new List<string> { catalog };
        files.AddRange(XDocument.Load(catalog).Root!.Elements(Fots + "test-set")
            .Select(ts => Path.Combine(suite, ts.Attribute("file")!.Value)));
        foreach (var file in files)
        {
            foreach (var element in XDocument.Load(file).Descendants().Where(e => e.Name.Namespace == Fots))
            {
                used.Add(element.Name.LocalName);
                foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
                    used.Add($"{element.Name.LocalName}@{attribute.Name}");
            }
        }

        // The inventory must have seen the catalog, or an empty set would pass.
        used.Should().Contain(["test-case", "environment", "assert-eq"]);

        var unaccounted = used.Where(n => !HarnessVocabulary.Handled.Contains(n)
            && !HarnessVocabulary.Ignored.ContainsKey(n)
            && !HarnessVocabulary.KnownGaps.ContainsKey(n)).ToList();
        unaccounted.Should().BeEmpty(
            "every catalog feature must be handled by XqtsTestRunner, ignored with a reason, or listed as a known gap; " +
            "a feature in none of these silently mis-scores the cases that use it");
    }

    [Fact]
    public void The_buckets_do_not_overlap()
    {
        HarnessVocabulary.Handled.Intersect(HarnessVocabulary.Ignored.Keys).Should().BeEmpty();
        HarnessVocabulary.Handled.Intersect(HarnessVocabulary.KnownGaps.Keys).Should().BeEmpty();
        HarnessVocabulary.Ignored.Keys.Intersect(HarnessVocabulary.KnownGaps.Keys).Should().BeEmpty();
    }
}
