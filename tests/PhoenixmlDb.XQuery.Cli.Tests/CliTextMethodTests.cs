using System.Diagnostics;
using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Cli.Tests;

/// <summary>
/// The CLI's text output method (<c>-o text</c>, <c>declare option output:method "text"</c>) is the
/// library's: Serialization 3.1 §8, the string values of the text nodes of the result, adjacent
/// atomic values separated by a space. The CLI had its own text serializer, which wrote an element or
/// a document as XML (<c>&lt;a&gt;x&lt;/a&gt;</c> for <c>&lt;a&gt;x&lt;/a&gt;</c>, also in 3.2.0).
/// </summary>
/// <remarks>
/// One rule of the CLI is not the library's: an attribute at the top of the result is written as its
/// string value, as the CLI always wrote it (<c>-o text //@href</c>); fn:serialize gives SENR0001 for
/// it. Arrays are flattened first, as sequence normalization flattens them (§2 step 1), so an attribute
/// in an array is at the top too. So each case is compared with <c>fn:serialize</c> of the input with
/// its arrays flattened and each top-level attribute replaced by its string value; an input the library
/// refuses is the same error.
/// </remarks>
public sealed class CliTextMethodTests
{
    private static (int Exit, string Out, string Err) RunCli(params string[] args)
    {
        var cli = typeof(OutputMethods).Assembly.Location;
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(cli);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

    private static string Line(string text) => text.Length > 0 ? text + Environment.NewLine : text;

    public static TheoryData<string, bool> Inputs()
    {
        var data = new TheoryData<string, bool>();
        foreach (var input in new[]
        {
            "<a>x<b>y</b>z</a>",                                        // an element
            "<a b='v'>x</a>",                                           // an attribute of an element is not text
            "document { <a>x</a>, <b>y</b> }",                          // a document
            "(<a>x</a>, <b>y</b>)",                                     // a sequence of nodes
            "(1, 'a', 2.5)",                                            // atomic values
            "(<a>x</a>, 1, 2, <b>y</b>, 'z')",                          // nodes and atomic values
            "text { 't' }",                                             // a text node
            "(comment { 'c' }, processing-instruction p { 'q' }, <a>x</a>)", // comments and PIs
            "attribute a { 'v' }",                                      // an attribute alone: its value
            "(attribute a { 'v' }, attribute b { 'w' })",               // two attributes: two values, a space
            "(attribute a { 'v' }, <b>x</b>)",                          // an attribute next to an element
            "(<b>x</b>, attribute a { 'v' }, 'z')",                     // an attribute next to an atomic value
            "[attribute a { 'v' }, [attribute b { 'w' }]]",             // arrays are flattened first (§2 step 1)
            "(attribute a { 'v' }, [])",                                // an empty array
        })
        {
            data.Add(input, false);
            data.Add(input, true);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public async Task The_text_method_writes_what_fn_serialize_writes(string input, bool fromProlog)
    {
        string? expected = null;
        string? expectedCode = null;
        try
        {
            expected = await new XQueryFacade().EvaluateAsync(
                $"serialize(array:flatten(({input})) ! (if (. instance of attribute()) then string(.) else .), map {{ 'method': 'text' }})");
        }
        catch (XQueryRuntimeException e)
        {
            expectedCode = e.ErrorCode;
        }

        var (exit, stdout, stderr) = fromProlog
            ? RunCli("declare option output:method \"text\"; " + input)
            : RunCli("-o", "text", input);

        if (expectedCode is not null)
        {
            exit.Should().NotBe(0, "fn:serialize gives {0}", expectedCode);
            stderr.Should().Contain(expectedCode);
            stdout.Should().BeEmpty();
            return;
        }
        exit.Should().Be(0, stderr);
        stdout.Should().Be(Line(expected!), "fn:serialize gives \"{0}\" for {1}", expected, input);
    }

    // The case of the report, in words: an element is its text.
    [Fact]
    public void An_element_is_written_as_its_text()
    {
        var (exit, stdout, stderr) = RunCli("-o", "text", "<a>x<b>y</b></a>");
        exit.Should().Be(0, stderr);
        stdout.Should().Be(Line("xy"));
    }

    // The attributes of a document, as 3.2.0 wrote them: the values, one space between two of them.
    [Fact]
    public void The_attributes_of_a_document_are_written_as_their_values()
    {
        var dir = Directory.CreateTempSubdirectory("phoenixml-cli-text-");
        try
        {
            var doc = Path.Combine(dir.FullName, "doc.xml");
            File.WriteAllText(doc, "<r><a href='h1'/><a href='h2'/></r>");
            var (exit, stdout, stderr) = RunCli("-o", "text", "//@href", doc);
            exit.Should().Be(0, stderr);
            stdout.Should().Be(Line("h1 h2"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("-o", "text")]
    [InlineData(null, null)]
    public void An_attribute_alone_is_written_as_its_value(string? option, string? method)
    {
        var (exit, stdout, stderr) = option is null
            ? RunCli("declare option output:method \"text\"; attribute a { 'v' }")
            : RunCli(option, method!, "attribute a { 'v' }");
        exit.Should().Be(0, stderr);
        stdout.Should().Be(Line("v"));
    }

    // Guard: the rule is the CLI's. fn:serialize still gives SENR0001 (Serialization 3.1 §2 step 7).
    [Fact]
    public async Task Fn_serialize_still_refuses_an_attribute()
    {
        var error = await Record.ExceptionAsync(() =>
            new XQueryFacade().EvaluateAsync("serialize(attribute a { 'v' }, map { 'method': 'text' })"));
        error.Should().BeOfType<XQueryRuntimeException>().Which.ErrorCode.Should().Be("SENR0001");
    }
}
