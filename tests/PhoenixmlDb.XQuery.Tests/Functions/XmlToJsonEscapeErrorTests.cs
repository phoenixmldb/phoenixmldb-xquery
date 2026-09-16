using System.Text;
using FluentAssertions;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// fn:xml-to-json validates the escape sequences of a string marked escaped="true", and a bad one is FOJS0007
/// (XPath F&amp;O 3.1 §17.5.1). Every such failure reported FOJS0006, the code for an input that is not a valid XML
/// representation of JSON (W3C xml-to-json-074 to -078, -C-017, -C-018).
/// </summary>
public sealed class XmlToJsonEscapeErrorTests
{
    private static async Task<string> ErrorCodeOfAsync(string escapedText)
    {
        var query = "declare namespace fn = \"http://www.w3.org/2005/xpath-functions\"; "
            + $"fn:xml-to-json(<fn:string escaped=\"true\">{escapedText}</fn:string>)";
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        try
        {
            var sb = new StringBuilder();
            await foreach (var item in engine.ExecuteAsync(new XQueryParserFacade().Parse(query)))
                sb.Append(item?.ToString());
            return $"no error, returned '{sb}'";
        }
        // context.Error(...) in the function layer builds PhoenixmlDb.XQuery.Functions.XQueryException.
        catch (PhoenixmlDb.XQuery.Functions.XQueryException ex)
        {
            return ex.ErrorCode ?? ex.Message;
        }
        catch (XQueryRuntimeException ex)
        {
            return ex.ErrorCode ?? ex.Message;
        }
    }

    [Theory]
    [InlineData("\\Q", "an unknown escape")]
    [InlineData("\\uDEFG", "a \\u escape with a non-hex digit")]
    [InlineData("\\uABC", "an incomplete \\u escape")]
    [InlineData("\\", "a lone backslash at the end")]
    public async Task A_bad_escape_in_an_escaped_string_is_FOJS0007(string text, string why)
        => (await ErrorCodeOfAsync(text)).Should().Be("FOJS0007", why);

    [Fact]
    public async Task A_valid_escape_is_accepted()
        => (await ErrorCodeOfAsync("a\\tb\\u0041c")).Should().StartWith("no error");
}
