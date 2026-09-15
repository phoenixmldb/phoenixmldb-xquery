using FluentAssertions;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// A leading lone '/' followed by a token that could start a relative path is XPST0003. The parser threw
/// PhoenixmlDb.Core.XQueryException with its (message, errorCode) arguments in code-first order, so ErrorCode held the
/// sentence and Message held "XPST0003" (xquery#62; QT3 PathExpr-3, -5 and 20 more).
/// </summary>
public sealed class LeadingSlashErrorCodeTests
{
    // QT3 PathExpr-3 and PathExpr-5: a lone '/' followed by '*' or '<', which could start a relative path.
    [Theory]
    [InlineData("fn:count(.[/ * 5])")]
    [InlineData("fn:count(.[/ < 5])")]
    public void A_leading_lone_slash_reports_XPST0003_as_its_error_code(string query)
    {
        var ex = Record.Exception(() => new XQueryParserFacade().Parse(query));
        ex.Should().NotBeNull();
        var code = ex switch
        {
            PhoenixmlDb.Core.XQueryException core => core.ErrorCode,
            PhoenixmlDb.XQuery.Functions.XQueryException fn => fn.ErrorCode,
            _ => null,
        };
        code.Should().Be("XPST0003", because: $"{ex!.GetType().FullName}: {ex.Message}");
        ex.Message.Should().Contain("Leading lone '/'");
    }
}
