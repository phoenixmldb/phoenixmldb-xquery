using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// Regression tests for direct attribute value literal brace escaping
/// (XQuery 3.1 §3.9.1.1). The escape forms are <c>{{</c> for a literal
/// <c>{</c> and <c>}}</c> for a literal <c>}</c>.
///
/// Originated from Martin Honnen's report: <c>&lt;element foo='{{.}}'/&gt;</c>
/// failed with "Unmatched '}' in direct attribute value literal — use '}}' to
/// escape" because ANTLR's longest-match rule let ATTR_*_CHAR greedily swallow
/// the trailing <c>.}}</c> as one token, and <c>DecodeAttrContent</c> then
/// rejected the bare-looking '}'. The fix pairs '}}' inside that decoder.
/// </summary>
public class AttrBraceEscapeTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task Attr_with_escaped_braces_around_dot_double_quoted()
    {
        var result = await _facade.EvaluateAsync("<element foo=\"{{.}}\"/>");
        result.Should().Be("<element foo=\"{.}\"/>");
    }

    [Fact]
    public async Task Attr_with_escaped_braces_around_dot_single_quoted()
    {
        var result = await _facade.EvaluateAsync("<element foo='{{.}}'/>");
        result.Should().Be("<element foo=\"{.}\"/>");
    }

    [Fact]
    public async Task Attr_with_only_escaped_open_brace()
    {
        var result = await _facade.EvaluateAsync("<element foo=\"{{x\"/>");
        result.Should().Be("<element foo=\"{x\"/>");
    }

    [Fact]
    public async Task Attr_with_only_escaped_close_brace()
    {
        var result = await _facade.EvaluateAsync("<element foo=\"x}}\"/>");
        result.Should().Be("<element foo=\"x}\"/>");
    }

    [Fact]
    public async Task Attr_with_three_close_braces_treats_as_escaped_plus_bare()
    {
        // '}}}' = escaped '}' + bare '}'. Bare '}' must error per XQuery spec.
        var act = async () => await _facade.EvaluateAsync("<element foo=\"a}}}\"/>");
        var ex = await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Parser.XQueryParseException>();
        ex.Which.Message.Should().Contain("XPST0003");
        ex.Which.Message.Should().Contain("'}'");
    }

    [Fact]
    public async Task Attr_with_enclosed_expression_among_escaped_braces()
    {
        // {{ literal { , then enclosed expr {1+2}, then }} literal }
        var result = await _facade.EvaluateAsync("<element foo=\"{{value={1+2}}}\"/>");
        result.Should().Be("<element foo=\"{value=3}\"/>");
    }
}
