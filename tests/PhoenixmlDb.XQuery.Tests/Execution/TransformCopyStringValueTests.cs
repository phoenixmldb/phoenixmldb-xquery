using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// The nodes copy/modify/return produces have string values, and an update changes them.
/// </summary>
/// <remarks>
/// Copies used to carry no string value and no way to compute one, so comparisons and
/// <c>data()</c> over them saw "" (or threw under Core's StrictStringValue) while
/// <c>string()</c>, which walks the children, was right: <c>$copy/a[. = 'e']</c> matched
/// nothing. The modify-clause cases also catch a value cached before the update and never
/// cleared: the condition reads the old value, the return clause must see the new one.
/// </remarks>
public class TransformCopyStringValueTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("count((copy $c := <r><a>w</a><a>e</a></r> modify () return $c)/a[. = 'e'])", "1")]
    [InlineData("data(copy $c := <a><b>1</b></a> modify () return $c) = '1'", "true")]
    [InlineData("(copy $c := document { <a><b>x</b></a> } modify () return $c) = 'x'", "true")]
    [InlineData("(copy $c := <a><b>2</b></a> modify () return $c) + 1 = 3", "true")]
    public async Task Copy_atomizes_to_its_text(string query, string expected)
    {
        (await _facade.EvaluateAsync(query)).Should().Be(expected);
    }

    [Theory]
    [InlineData("(copy $c := <a><b>x</b></a> modify insert node <b>z</b> into $c return $c) = 'xz'")]
    [InlineData("(copy $c := <a><b>x</b></a> modify insert node <b>z</b> as first into $c return $c) = 'zx'")]
    [InlineData("(copy $c := <a><b>x</b><b>y</b></a> modify delete node $c/b[1] return $c) = 'y'")]
    [InlineData("(copy $c := <a><b>x</b></a> modify replace value of node $c/b with 'q' return $c) = 'q'")]
    [InlineData("(copy $c := <a><b>x</b></a> modify replace node $c/b with <c>r</c> return $c) = 'r'")]
    [InlineData("(copy $c := <a><b>x</b></a> modify rename node $c/b as 'q' return $c)/q = 'x'")]
    public async Task Update_changes_the_value(string query)
    {
        (await _facade.EvaluateAsync(query)).Should().Be("true");
    }

    [Theory]
    [InlineData("copy $c := <a><b>x</b></a> modify (if ($c = 'x') then replace value of node $c/b with 'y' else ()) return $c = 'y'")]
    [InlineData("copy $c := <a><b>x</b><b>y</b></a> modify (if ($c/b[1] = 'x' and $c = 'xy') then delete node $c/b[1] else ()) return $c = 'y'")]
    [InlineData("copy $c := <a><b>x</b></a> modify (if ($c/b = 'x') then insert node <b>z</b> into $c else ()) return $c = 'xz'")]
    public async Task Value_read_in_modify_does_not_outlive_the_update(string query)
    {
        (await _facade.EvaluateAsync(query)).Should().Be("true");
    }

    [Theory]
    [InlineData("data(json-to-xml('[\"x\", \"y\"]')) = 'xy'")]
    [InlineData("json-to-xml('{\"a\": \"v\"}') = 'v'")]
    public async Task Json_to_xml_document_atomizes_to_its_text(string query)
    {
        (await _facade.EvaluateAsync(query)).Should().Be("true");
    }
}
