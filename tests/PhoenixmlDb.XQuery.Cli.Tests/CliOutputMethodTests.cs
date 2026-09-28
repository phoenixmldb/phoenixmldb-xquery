using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Cli.Tests;

/// <summary>
/// The CLI's output methods. The -o flag and the query's `declare option output:method` each had
/// their own switch; both knew only adaptive/xml/text/json and mapped ANYTHING else to adaptive.
/// So -o html, output:method "html" and a typo such as -o xmll all silently produced adaptive
/// output. Now there is one parser, html and xhtml are served by the engine's serializer, and an
/// unknown name is an error.
///
/// The process tests matter as much as the parser tests: the defect was in the WIRING, two local
/// switches, and a test of the parser alone would pass with either of them put back.
/// </summary>
public sealed class CliOutputMethodTests
{
    [Theory]
    [InlineData("adaptive", OutputMethod.Adaptive)]
    [InlineData("xml", OutputMethod.Xml)]
    [InlineData("text", OutputMethod.Text)]
    [InlineData("json", OutputMethod.Json)]
    [InlineData("html", OutputMethod.Html)]
    [InlineData("xhtml", OutputMethod.Xhtml)]
    [InlineData(" HTML ", OutputMethod.Html)]
    internal void Every_method_name_parses(string name, OutputMethod expected)
        => OutputMethods.Parse(name).Should().Be(expected);

    [Theory]
    [InlineData("xmll")]
    [InlineData("htm")]
    [InlineData("")]
    public void An_unknown_name_is_not_guessed(string name)
        => OutputMethods.Parse(name).Should().BeNull();

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

    private const string Page = """<html><body><p>a<br/>b</p><img src="x"/></body></html>""";

    [Fact]
    public void Dash_o_html_writes_html()
    {
        var (exit, stdout, _) = RunCli("-o", "html", Page);
        exit.Should().Be(0);
        stdout.Should().Contain("<br>").And.Contain("""<img src="x">""").And.NotContain("<br/>");
    }

    [Fact]
    public void Dash_o_xhtml_writes_xhtml()
    {
        var (exit, stdout, _) = RunCli("-o", "xhtml", Page);
        exit.Should().Be(0);
        // <br /> alone cannot tell xhtml from the old silent fallback: the adaptive XML writer
        // emits it too. The DOCTYPE is what only the html/xhtml methods write.
        stdout.Should().Contain("<!DOCTYPE html>").And.Contain("<br />");
    }

    [Fact]
    public void The_query_option_selects_html_too()
    {
        var (exit, stdout, _) = RunCli("declare option output:method \"html\"; " + Page);
        exit.Should().Be(0);
        stdout.Should().Contain("<br>").And.NotContain("<br/>");
    }

    [Fact]
    public void An_unknown_dash_o_method_is_an_error_not_adaptive()
    {
        var (exit, stdout, stderr) = RunCli("-o", "xmll", "1+1");
        exit.Should().NotBe(0);
        stderr.Should().Contain("unknown output method 'xmll'");
        stdout.Should().BeEmpty("nothing may be produced under a guessed method");
    }

    [Fact]
    public void An_unknown_query_option_method_is_SEPM0016()
    {
        var (exit, _, stderr) = RunCli("declare option output:method \"htm\"; 1");
        exit.Should().NotBe(0);
        stderr.Should().Contain("SEPM0016");
    }
}
