using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Indented serialization writes LF on every OS, not Environment.NewLine (CRLF on Windows). Only
/// the Windows CI job can fail this without the fix.
/// </summary>
public sealed class IndentNewlineTests
{
    [Fact]
    public async Task Indented_serialization_uses_LF()
    {
        var result = await new XQueryFacade().EvaluateAsync(
            "fn:serialize(<a><b/><c><d/></c></a>, map { 'method': 'xml', 'indent': true() })", "<x/>");
        result.Should().Contain("\n").And.NotContain("\r");
    }
}
