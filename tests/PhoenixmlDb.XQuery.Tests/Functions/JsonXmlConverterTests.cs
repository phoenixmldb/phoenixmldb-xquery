using FluentAssertions;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// <see cref="JsonXmlConverter"/>: fn:json-to-xml's mapping as an API, for a host that stores or
/// exchanges JSON as XML.
/// </summary>
public sealed class JsonXmlConverterTests
{
    private const string Fn = "http://www.w3.org/2005/xpath-functions";

    // One array of one item, and a key twice: the input three converters disagreed on.
    private const string Sample = """{"tags":["a"],"n":1,"n":2}""";

    [Fact]
    public void An_array_of_one_item_stays_an_array_and_a_number_a_number()
        => JsonXmlConverter.ToXmlText("""{"tags":["a"],"n":1}""").Should().Be(
            $"""<map xmlns="{Fn}"><array key="tags"><string>a</string></array><number key="n">1</number></map>""");

    [Fact]
    public void A_repeated_key_is_retained_by_default_as_the_function_does()
        => JsonXmlConverter.ToXmlText(Sample).Should().Be(
            $"""<map xmlns="{Fn}"><array key="tags"><string>a</string></array><number key="n">1</number><number key="n">2</number></map>""");

    [Fact]
    public void UseFirst_keeps_the_first_entry_of_a_repeated_key()
        => JsonXmlConverter.ToXmlText(Sample, new JsonToXmlOptions { Duplicates = JsonDuplicateKeys.UseFirst }).Should().Be(
            $"""<map xmlns="{Fn}"><array key="tags"><string>a</string></array><number key="n">1</number></map>""");

    [Fact]
    public void Reject_makes_a_repeated_key_FOJS0003()
    {
        var act = () => JsonXmlConverter.ToXmlText(Sample, new JsonToXmlOptions { Duplicates = JsonDuplicateKeys.Reject });
        act.Should().Throw<XQueryException>().Where(e => e.ErrorCode == "FOJS0003");
    }

    [Theory]
    [InlineData("null", "<null xmlns=\"" + Fn + "\"/>")]
    [InlineData("true", "<boolean xmlns=\"" + Fn + "\">true</boolean>")]
    [InlineData("1.5e3", "<number xmlns=\"" + Fn + "\">1.5e3</number>")]
    [InlineData("\"a<b\"", "<string xmlns=\"" + Fn + "\">a&lt;b</string>")]
    [InlineData("[]", "<array xmlns=\"" + Fn + "\"/>")]
    [InlineData("{}", "<map xmlns=\"" + Fn + "\"/>")]
    public void Every_kind_of_value_has_its_element(string json, string xml)
        => JsonXmlConverter.ToXmlText(json).Should().Be(xml);

    [Fact]
    public void Text_that_is_not_JSON_is_FOJS0001()
    {
        var act = () => JsonXmlConverter.ToXmlText("""{"a":""");
        act.Should().Throw<XQueryException>().Where(e => e.ErrorCode == "FOJS0001");
    }

    [Fact]
    public void Liberal_accepts_a_trailing_comma_and_the_default_does_not()
    {
        JsonXmlConverter.ToXmlText("[1,]", new JsonToXmlOptions { Liberal = true })
            .Should().Be($"""<array xmlns="{Fn}"><number>1</number></array>""");
        var act = () => JsonXmlConverter.ToXmlText("[1,]");
        act.Should().Throw<XQueryException>().Where(e => e.ErrorCode == "FOJS0001");
    }

    [Fact]
    public void A_character_XML_cannot_hold_becomes_the_replacement_character()
        => JsonXmlConverter.ToXmlText("\"a\\u0000b\"").Should().Be("<string xmlns=\"" + Fn + "\">a\uFFFDb</string>");

    [Fact]
    public void A_fallback_says_what_takes_such_a_characters_place()
        => JsonXmlConverter.ToXmlText("\"a\\u0000b\"", new JsonToXmlOptions { Fallback = escaped => "[" + escaped + "]" })
            .Should().Be($"""<string xmlns="{Fn}">a[\u0000]b</string>""");

    [Fact]
    public void Escape_keeps_the_JSON_escapes_and_marks_the_string()
        => JsonXmlConverter.ToXmlText("\"a\\u0000b\"", new JsonToXmlOptions { Escape = true })
            .Should().Be($"""<string xmlns="{Fn}" escaped="true">a\u0000b</string>""");

    [Fact]
    public void Escape_and_a_fallback_together_are_refused()
    {
        var act = () => JsonXmlConverter.ToXmlText("1", new JsonToXmlOptions { Escape = true, Fallback = s => s });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_document_has_the_base_uri_asked_for()
    {
        var store = new XdmDocumentStore();
        var document = JsonXmlConverter.ToXml("1", store, new JsonToXmlOptions { BaseUri = new Uri("urn:test:doc.json") });
        document.BaseUri.Should().Be("urn:test:doc.json");
        store.GetNode(document.Id).Should().BeSameAs(document);
    }

    [Theory]
    [InlineData("""{"tags":["a"],"n":1,"n":2}""")]
    [InlineData("""{"a":{"b":[1,2.50,null,true,"x\ty"]},"k\"q":"é"}""")]
    [InlineData("""[[],{},""]""")]
    public async Task It_builds_what_the_function_builds(string json)
    {
        var facade = new XQueryFacade();
        var fromFunction = await facade.EvaluateAsync(
            $"serialize(json-to-xml('{json.Replace("'", "''", StringComparison.Ordinal)}'))");
        JsonXmlConverter.ToXmlText(json).Should().Be(fromFunction);
    }

    [Theory]
    [InlineData("""{"tags":["a"],"n":1}""")]
    [InlineData("""[1,"two",null,false,{"k":[]}]""")]
    public async Task The_function_turns_it_back_into_the_JSON(string json)
    {
        var xml = JsonXmlConverter.ToXmlText(json);
        var facade = new XQueryFacade();
        (await facade.EvaluateAsync("xml-to-json(.)", xml)).Should().Be(json);
    }
}
