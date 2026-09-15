using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// XQueryFacade.DetectSerializationOptions with a default output method. The facade serializes a query
/// that declares no method with adaptive; XQuery 3.1's default is xml, and a caller such as the QT3
/// runner cannot apply that itself because SerializationOptions does not record whether the method was
/// declared (QT3 K2-Serialization-1..4, -11).
/// </summary>
public sealed class DefaultOutputMethodDetectionTests : IDisposable
{
    private const string Output = "declare namespace output = \"http://www.w3.org/2010/xslt-xquery-serialization\"; ";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-defmethod-" + Guid.NewGuid().ToString("N"));

    public DefaultOutputMethodDetectionTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "text.xml"), """
            <output:serialization-parameters xmlns:output="http://www.w3.org/2010/xslt-xquery-serialization">
              <output:method value="text"/>
            </output:serialization-parameters>
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private Uri BaseUri => new(_dir + Path.DirectorySeparatorChar);

    [Fact]
    public void An_undeclared_method_takes_the_default()
        => XQueryFacade.DetectSerializationOptions("<a/>", staticBaseUri: null, OutputMethod.Xml)
            .Method.Should().Be(OutputMethod.Xml);

    [Fact]
    public void A_declared_method_overrides_the_default()
        => XQueryFacade.DetectSerializationOptions(Output + "declare option output:method \"json\"; map{}", staticBaseUri: null, OutputMethod.Xml)
            .Method.Should().Be(OutputMethod.Json);

    [Fact]
    public void A_declared_adaptive_method_overrides_a_non_adaptive_default()
        => XQueryFacade.DetectSerializationOptions(Output + "declare option output:method \"adaptive\"; 1", staticBaseUri: null, OutputMethod.Xml)
            .Method.Should().Be(OutputMethod.Adaptive);

    [Fact]
    public void A_parameter_document_method_overrides_the_default()
        => XQueryFacade.DetectSerializationOptions(Output + "declare option output:parameter-document \"text.xml\"; 1", BaseUri, OutputMethod.Xml)
            .Method.Should().Be(OutputMethod.Text);

    [Fact]
    public void Other_declarations_still_apply_with_a_default_method()
        => XQueryFacade.DetectSerializationOptions(Output + "declare option output:indent \"yes\"; <a/>", staticBaseUri: null, OutputMethod.Xml)
            .Should().Match<SerializationOptions>(o => o.Method == OutputMethod.Xml && o.Indent);

    [Fact]
    public void The_existing_overloads_still_default_to_adaptive()
    {
        XQueryFacade.DetectSerializationOptions("<a/>").Method.Should().Be(OutputMethod.Adaptive);
        XQueryFacade.DetectSerializationOptions("<a/>", staticBaseUri: null).Method.Should().Be(OutputMethod.Adaptive);
    }
}
