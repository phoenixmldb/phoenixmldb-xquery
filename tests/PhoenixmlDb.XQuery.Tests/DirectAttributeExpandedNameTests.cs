using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Two attributes of a direct element constructor with one expanded name are XQST0040, whatever
/// their prefixes. The static check compared lexical names, so foo:attr and bar:attr with both
/// prefixes bound to one URI got past it and failed at run time as XQDY0025 (QT3
/// Constr-attr-distnames-4, K2-DefaultNamespaceProlog-10).
/// </summary>
public class DirectAttributeExpandedNameTests
{
    private static IEnumerable<string> Errors(string query)
    {
        var compiled = new QueryEngine().Compile(query);
        return compiled.Success ? [] : compiled.Errors.Select(e => e.Code);
    }

    [Theory]
    [InlineData("<e xmlns:foo='urn:x' xmlns:bar='urn:x' foo:a='1' bar:a='2'/>")]
    [InlineData("declare namespace a = 'urn:x'; <e xmlns:b='urn:x' a:n='1' b:n='2'/>")]
    [InlineData("<e a='1' a='2'/>")]
    public void Attributes_with_one_expanded_name_are_XQST0040(string query)
        => Errors(query).Should().Contain("XQST0040");

    [Theory]
    [InlineData("<e xmlns:foo='urn:x' xmlns:bar='urn:y' foo:a='1' bar:a='2'/>")]
    [InlineData("<e xmlns:foo='urn:x' a='1' foo:a='2'/>")]
    public void Attributes_with_distinct_expanded_names_compile(string query)
        => Errors(query).Should().BeEmpty();
}
