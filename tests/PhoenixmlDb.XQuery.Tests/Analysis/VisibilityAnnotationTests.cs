using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Analysis;

/// <summary>
/// %public and %private are names in the XQuery namespace, however they are written; and an
/// import's target namespace must not be empty.
/// </summary>
/// <remarks>
/// Only the bare spelling was recognised, so <c>%private %xq:public</c> (xq bound to the XQuery
/// namespace) was rejected as an unknown annotation in a reserved namespace, XQST0045, instead of
/// the public/private conflict, XQST0106 for a function and XQST0116 for a variable (QT3
/// modules-pub-priv-30/34). And <c>import module ""</c> reported that no module could be found,
/// XQST0059, instead of XQST0088 (K-ModuleImport-1/2, XQST0088_1).
/// </remarks>
public class VisibilityAnnotationTests
{
    private readonly XQueryFacade _facade = new();
    private const string Xq = "declare namespace xq = 'http://www.w3.org/2012/xquery'; ";

    private async Task<string> Outcome(string query)
    {
        try { return "value " + await _facade.EvaluateAsync(query); }
        catch (PhoenixmlDb.XQuery.Parser.XQueryParseException e) { return ExtractCode(e.Message); }
        catch (PhoenixmlDb.XQuery.Execution.XQueryRuntimeException e) { return e.ErrorCode; }
        catch (PhoenixmlDb.XQuery.Functions.XQueryException e) { return e.ErrorCode; }
    }

    private static string ExtractCode(string message)
    {
        var m = System.Text.RegularExpressions.Regex.Match(message, "X[PQ][A-Z]{2}[0-9]{4}");
        return m.Success ? m.Value : message;
    }

    [Theory]
    [InlineData(Xq + "declare %private %xq:public function local:f() { 1 }; local:f()", "XQST0106")]
    [InlineData(Xq + "declare %private %xq:public variable $v := 1; $v", "XQST0116")]
    [InlineData("declare %Q{http://www.w3.org/2012/xquery}private %public function local:f() { 1 }; local:f()", "XQST0106")]
    [InlineData("import module \"\"; 1", "XQST0088")]
    // Controls: the bare spellings, the qualified spelling on its own, and another namespace.
    [InlineData("declare %private %public function local:f() { 1 }; local:f()", "XQST0106")]
    [InlineData(Xq + "declare %xq:private function local:f() { 1 }; local:f()", "value 1")]
    [InlineData("declare namespace x = 'urn:x'; declare %x:public function local:f() { 1 }; local:f()", "value 1")]
    public async Task VisibilityAndImportErrors(string query, string expected)
        => (await Outcome(query)).Should().Be(expected);
}
