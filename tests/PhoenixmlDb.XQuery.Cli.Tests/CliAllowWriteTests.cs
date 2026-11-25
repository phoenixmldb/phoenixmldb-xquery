using System.Diagnostics;
using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Cli.Tests;

/// <summary>
/// <c>--allow-write &lt;directory&gt;</c>: fn:put may write in that directory and below it (after
/// links are resolved), nowhere else; without the option fn:put is refused (Lucas, 2026-11-20).
/// </summary>
public sealed class CliAllowWriteTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-xquery-allow-write").FullName;
    private readonly string _out;
    private readonly string _other;

    public CliAllowWriteTests()
    {
        _out = Directory.CreateDirectory(Path.Combine(_dir, "out")).FullName;
        _other = Directory.CreateDirectory(Path.Combine(_dir, "other")).FullName;
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
    public void A_put_in_the_allowed_directory_is_written()
    {
        var (exit, _, err) = RunCli("--allow-write", _out, "fn:put(<a>1</a>, 'out/a.xml')");

        exit.Should().Be(0, err);
        File.ReadAllText(Path.Combine(_out, "a.xml")).Should().Contain("<a>1</a>");
    }

    [Fact]
    public void The_option_can_be_given_more_than_once()
    {
        var (exit, _, err) = RunCli($"--allow-write={_out}", "--allow-write", _other,
            "(fn:put(<a/>, 'out/a.xml'), fn:put(<b/>, 'other/b.xml'))");

        exit.Should().Be(0, err);
        File.Exists(Path.Combine(_out, "a.xml")).Should().BeTrue();
        File.Exists(Path.Combine(_other, "b.xml")).Should().BeTrue();
    }

    [Fact]
    public void Without_the_option_a_put_is_refused_and_nothing_is_written()
    {
        var (exit, _, err) = RunCli("fn:put(<a/>, 'out/a.xml')");

        exit.Should().NotBe(0);
        err.Should().Contain("XUDY9001").And.Contain("--allow-write");
        Directory.GetFiles(_out).Should().BeEmpty();
    }

    [Fact]
    public void A_put_outside_the_allowed_directory_is_refused()
    {
        var (exit, _, err) = RunCli("--allow-write", _out, "fn:put(<a/>, 'other/a.xml')");

        exit.Should().NotBe(0);
        err.Should().Contain("XUDY9001");
        Directory.GetFiles(_other).Should().BeEmpty();
    }

    [Fact]
    public void An_allowed_directory_that_does_not_exist_is_an_error()
    {
        var (exit, _, err) = RunCli("--allow-write", Path.Combine(_dir, "missing"), "1");

        exit.Should().Be(1);
        err.Should().Contain("--allow-write").And.Contain("is not a directory");
    }
}
