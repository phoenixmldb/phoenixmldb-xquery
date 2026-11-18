using System.Diagnostics;
using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Cli.Tests;

/// <summary>
/// JSON sources for the CLI: a *.json file is its map or array, a directory's JSON files join
/// collection(), and JSON on stdin is read as JSON. A source that cannot be loaded (not
/// well-formed, not valid JSON, missing) is one line on stderr and exit code 1: it escaped to a
/// handler that rethrew it, and the process ended with a stack trace.
/// </summary>
public sealed class CliJsonSourceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-cli-json").FullName;

    public CliJsonSourceTests()
    {
        var orders = Directory.CreateDirectory(Path.Combine(_dir, "orders")).FullName;
        var sub = Directory.CreateDirectory(Path.Combine(orders, "sub")).FullName;
        File.WriteAllText(Path.Combine(orders, "o1.json"), """{"id":1,"items":[{"name":"pen"},{"name":"ink"}]}""");
        File.WriteAllText(Path.Combine(sub, "o2.json"), """{"id":2,"items":[{"name":"pad"}]}""");
        File.WriteAllText(Path.Combine(orders, "o3.xml"), """<o id="3"/>""");
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{bad");
        File.WriteAllText(Path.Combine(_dir, "bad.xml"), "<bad");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private (int Exit, string Out, string Err) RunCli(string? stdin, params string[] args)
    {
        var cli = typeof(OutputMethods).Assembly.Location;
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            WorkingDirectory = _dir,
        };
        psi.ArgumentList.Add(cli);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        if (stdin is not null)
        {
            p.StandardInput.Write(stdin);
            p.StandardInput.Close();
        }
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout.Trim(), stderr.Trim());
    }

    [Fact]
    public void A_json_file_is_its_map()
    {
        var (exit, stdout, _) = RunCli(null, "string-join(?items?*?name, ' ')", Path.Combine("orders", "o1.json"));
        exit.Should().Be(0);
        stdout.Should().Be("pen ink");
    }

    [Fact]
    public void A_directory_gives_its_json_files_in_collection()
    {
        var (exit, stdout, _) = RunCli(null,
            "string-join(collection()[. instance of map(*)]?items?*?name, ' ') || ' / ' || count(collection())", "orders");
        exit.Should().Be(0);
        stdout.Should().Be("pen ink pad / 3");
    }

    [Fact]
    public void A_query_can_read_a_directory_with_saxon_parameters()
    {
        var (exit, stdout, _) = RunCli(null, "string-join(collection('orders?select=*.json;recurse=yes') ! string(?id), ' ')");
        exit.Should().Be(0);
        stdout.Should().Be("1 2");
    }

    [Fact]
    public void Json_on_stdin_is_read_as_json()
    {
        var (exit, stdout, _) = RunCli("""{"a":[1,2,3]}""", "string(sum(?a?*))");
        exit.Should().Be(0);
        stdout.Should().Be("6");
    }

    [Fact]
    public void Xml_on_stdin_is_still_read_as_xml()
    {
        var (exit, stdout, _) = RunCli("<a>5</a>", "string(.)");
        exit.Should().Be(0);
        stdout.Should().Be("5");
    }

    [Theory]
    [InlineData("bad.json", null)]
    [InlineData("bad.xml", null)]
    [InlineData("missing.xml", null)]
    [InlineData(null, "{bad")]
    public void A_source_that_cannot_be_loaded_is_one_line_and_exit_code_1(string? source, string? stdin)
    {
        var (exit, stdout, stderr) = source is null ? RunCli(stdin, ".") : RunCli(null, ".", source);

        exit.Should().Be(1);
        stdout.Should().BeEmpty();
        stderr.Should().StartWith("Error: Cannot load source '").And.NotContain("\n").And.NotContain(" at ");
    }
}
