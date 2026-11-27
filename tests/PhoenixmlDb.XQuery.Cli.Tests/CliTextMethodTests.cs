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
/// Each case is compared with <c>fn:serialize($x, map { 'method': 'text' })</c>; an input the library
/// refuses is the same error.
/// </summary>
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
            "attribute a { 'v' }",                                      // an attribute alone
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
            expected = await new XQueryFacade().EvaluateAsync($"serialize({input}, map {{ 'method': 'text' }})");
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
        stdout.Should().Be(expected!.Length > 0 ? expected + Environment.NewLine : expected,
            "fn:serialize gives \"{0}\" for {1}", expected, input);
    }

    // The case of the report, in words: an element is its text.
    [Fact]
    public void An_element_is_written_as_its_text()
    {
        var (exit, stdout, stderr) = RunCli("-o", "text", "<a>x<b>y</b></a>");
        exit.Should().Be(0, stderr);
        stdout.TrimEnd().Should().Be("xy");
    }
}
