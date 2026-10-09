using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// Several parse sites in the engines read a document with DTD processing on and set no
/// <see cref="XmlResolver"/>, relying on the runtime to resolve nothing external by default.
/// This pins that behaviour of the runtime: if a future runtime changes it, these fail, and
/// those sites must set the resolver to null themselves.
/// </summary>
public sealed class RuntimeDefaultEntityResolutionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-default-resolver").FullName;
    private readonly string _document;

    public RuntimeDefaultEntityResolutionTests()
    {
        var secret = Path.Combine(_dir, "secret.txt");
        File.WriteAllText(secret, "external-content");
        _document = $"<!DOCTYPE a [<!ENTITY e SYSTEM \"{new Uri(secret).AbsoluteUri}\">]><a>&e;</a>";
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Outcome(Func<string> read)
    {
        try { return read(); }
        catch (XmlException e) { return "error: " + e.Message; }
    }

    [Fact]
    public void A_reader_with_dtd_processing_and_no_resolver_set_reads_no_external_entity()
        => Outcome(() =>
        {
            using var reader = XmlReader.Create(new StringReader(_document), new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse });
            return XDocument.Load(reader).Root!.Value;
        }).Should().NotContain("external-content");

    [Fact]
    public void XDocument_Parse_reads_no_external_entity()
        => Outcome(() => XDocument.Parse(_document).Root!.Value).Should().NotContain("external-content");

    [Fact]
    public void XmlDocument_from_a_reader_with_no_resolver_set_reads_no_external_entity()
        => Outcome(() =>
        {
            using var reader = XmlReader.Create(new StringReader(_document), new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse });
            var document = new XmlDocument();
            document.Load(reader);
            return document.DocumentElement!.InnerText;
        }).Should().NotContain("external-content");
}
