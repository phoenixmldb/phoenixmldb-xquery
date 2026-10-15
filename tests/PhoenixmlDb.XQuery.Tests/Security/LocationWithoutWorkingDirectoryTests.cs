using AwesomeAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A location with a scheme is a URI or nothing: <c>file:/path</c> (one slash, RFC 8089) is a
/// file URI, and a text with a scheme that is not a URI resolves to nothing. Both were taken
/// for a relative path and made absolute against the process working directory. A schema
/// location hint is never resolved against the working directory.
/// </summary>
public class LocationWithoutWorkingDirectoryTests
{
    [Fact]
    public void A_one_slash_file_URI_is_a_file_URI()
        => ResourcePolicy.Resolve("file:/abs/x.xsd")!.AbsoluteUri.Should().Be("file:///abs/x.xsd");

    [Fact]
    public void A_text_with_a_scheme_that_is_not_a_URI_resolves_to_nothing()
        => ResourcePolicy.Resolve("http://[not-a-host/x.xsd").Should().BeNull();

    [Theory]
    [InlineData("relative/x.xsd")]
    [InlineData("http://[not-a-host/x.xsd")]
    public void A_schema_hint_is_not_made_absolute_against_the_working_directory(string hint)
    {
        var provider = new XsdSchemaProvider();
        var act = () => provider.ImportSchema("urn:t", [hint]);
        act.Should().Throw<SchemaException>().Which.Message.Should().NotContain(Environment.CurrentDirectory);
    }
}
