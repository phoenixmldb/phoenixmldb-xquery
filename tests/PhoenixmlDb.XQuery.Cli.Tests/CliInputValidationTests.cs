using System.Diagnostics;
using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Cli.Tests;

/// <summary>
/// <c>--validation strict|lax</c> (and <c>--val:strict</c>, <c>--val:lax</c>) validate the context
/// document before the query runs, as <see cref="Execution.QueryEngine.InputValidation"/> does
/// (#267). Without one the document stays untyped; before, the CLI had no way to ask for it.
/// </summary>
public sealed class CliInputValidationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-xquery-validation").FullName;
    private readonly string _query;

    public CliInputValidationTests()
    {
        File.WriteAllText(Path.Combine(_dir, "s.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="value" type="xs:decimal"/></xs:schema>
            """);
        File.WriteAllText(Path.Combine(_dir, "ok.xml"), "<value>1.5</value>");
        File.WriteAllText(Path.Combine(_dir, "bad.xml"), "<value></value>");
        File.WriteAllText(Path.Combine(_dir, "other.xml"), "<other/>");
        _query = Path.Combine(_dir, "q.xq");
        File.WriteAllText(_query, "import schema default element namespace '' at 's.xsd'; data(/value) instance of xs:decimal");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private (int Exit, string Out, string Err) RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _dir };
        psi.ArgumentList.Add(typeof(OutputMethods).Assembly.Location);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout.Trim(), stderr);
    }

    [Fact]
    public void Without_the_option_the_document_is_untyped()
    {
        var (exit, stdout, _) = RunCli("-f", _query, Path.Combine(_dir, "bad.xml"));
        exit.Should().Be(0);
        stdout.Should().Be("false");
    }

    [Theory]
    [InlineData("--validation", "strict")]
    [InlineData("--validation", "lax")]
    [InlineData("--val:strict", null)]
    [InlineData("--val:lax", null)]
    public void A_valid_document_is_typed(string option, string? value)
    {
        var (exit, stdout, stderr) = RunCli(value is null ? [option, "-f", _query, Path.Combine(_dir, "ok.xml")] : [option, value, "-f", _query, Path.Combine(_dir, "ok.xml")]);
        exit.Should().Be(0, stderr);
        stdout.Should().Be("true");
    }

    [Theory]
    [InlineData("--validation", "strict")]
    [InlineData("--validation", "lax")]
    [InlineData("--val:strict", null)]
    [InlineData("--val:lax", null)]
    public void An_invalid_document_is_XQDY0027(string option, string? value)
    {
        var (exit, _, stderr) = RunCli(value is null ? [option, "-f", _query, Path.Combine(_dir, "bad.xml")] : [option, value, "-f", _query, Path.Combine(_dir, "bad.xml")]);
        exit.Should().NotBe(0);
        stderr.Should().Contain("XQDY0027");
    }

    [Fact]
    public void Strict_validation_of_an_undeclared_element_is_XQDY0084()
    {
        var (exit, _, stderr) = RunCli("--validation", "strict", "-f", _query, Path.Combine(_dir, "other.xml"));
        exit.Should().NotBe(0);
        stderr.Should().Contain("XQDY0084");
    }

    [Fact]
    public void An_unknown_validation_value_is_refused()
    {
        var (exit, _, stderr) = RunCli("--validation", "preserve", "-f", _query, Path.Combine(_dir, "ok.xml"));
        exit.Should().Be(1);
        stderr.Should().Contain("strict or lax");
    }
}
