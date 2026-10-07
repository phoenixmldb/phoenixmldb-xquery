using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// The static rules on a schema import (XQuery 3.1 §4.11) hold whether or not a schema for the
/// namespace can be found. They were not checked, so each of these reported XQST0059 "no schema
/// location" instead, or compiled when a schema happened to be available.
/// </summary>
public sealed class SchemaImportStaticRuleTests
{
    private static string Compile(string query, bool withSchema = false)
    {
        var provider = new XsdSchemaProvider();
        if (withSchema)
            provider.AddFromString("urn:h",
                """<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:h"/>""");
        try
        {
            var compiled = new QueryEngine(schemaProvider: provider).Compile(query);
            return compiled.Success ? "ok" : string.Join("; ", compiled.Errors.Select(e => $"{e.Code}: {e.Message}"));
        }
        catch (XQueryParseException ex)
        {
            return ex.ErrorCode;
        }
    }

    [Theory]
    [InlineData("import schema namespace xml = 'urn:h'; 1", "XQST0070")]
    [InlineData("import schema namespace xmlns = 'urn:h'; 1", "XQST0070")]
    [InlineData("import schema namespace p = ''; 1", "XQST0057")]
    [InlineData("import schema namespace a = 'urn:h'; import schema namespace b = 'urn:h'; 1", "XQST0058")]
    [InlineData("import schema 'urn:h'; import schema default element namespace 'urn:h'; 1", "XQST0058")]
    public void A_broken_rule_is_reported_with_or_without_a_schema(string query, string expected)
    {
        Compile(query).Should().Be(expected);
        Compile(query, withSchema: true).Should().Be(expected);
    }

    [Theory]
    [InlineData("import schema namespace a = 'urn:h'; 1")]
    [InlineData("import schema default element namespace 'urn:h'; 1")]
    [InlineData("import schema 'urn:h'; 1")]
    public void A_single_import_of_an_available_schema_compiles(string query) =>
        Compile(query, withSchema: true).Should().Be("ok");
}
