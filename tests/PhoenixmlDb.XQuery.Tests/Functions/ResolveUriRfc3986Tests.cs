using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// fn:resolve-uri follows RFC 3986 §5.2. These are the RFC's own §5.4 examples against its base
/// URI "http://a/b/c/d;p?q": the normal ones, and the abnormal ones whose dot segments climb past
/// the root, which System.Uri resolution left in place.
/// </summary>
public sealed class ResolveUriRfc3986Tests
{
    private static async Task<string> Resolve(string reference)
    {
        var f = new XQueryFacade();
        var q = $"string(resolve-uri('{reference}', 'http://a/b/c/d;p?q'))";
        return (await f.EvaluateAsync(q)).Trim();
    }

    [Theory]
    // §5.4.1 normal examples
    [InlineData("g:h", "g:h")]
    [InlineData("g", "http://a/b/c/g")]
    [InlineData("./g", "http://a/b/c/g")]
    [InlineData("g/", "http://a/b/c/g/")]
    [InlineData("/g", "http://a/g")]
    [InlineData("//g", "http://g")]
    [InlineData("?y", "http://a/b/c/d;p?y")]
    [InlineData("g?y", "http://a/b/c/g?y")]
    [InlineData("#s", "http://a/b/c/d;p?q#s")]
    [InlineData("g#s", "http://a/b/c/g#s")]
    [InlineData("g?y#s", "http://a/b/c/g?y#s")]
    [InlineData(";x", "http://a/b/c/;x")]
    [InlineData("g;x", "http://a/b/c/g;x")]
    [InlineData("g;x?y#s", "http://a/b/c/g;x?y#s")]
    [InlineData("", "http://a/b/c/d;p?q")]
    [InlineData(".", "http://a/b/c/")]
    [InlineData("./", "http://a/b/c/")]
    [InlineData("..", "http://a/b/")]
    [InlineData("../", "http://a/b/")]
    [InlineData("../g", "http://a/b/g")]
    [InlineData("../..", "http://a/")]
    [InlineData("../../", "http://a/")]
    [InlineData("../../g", "http://a/g")]
    // §5.4.2 abnormal examples
    [InlineData("../../../g", "http://a/g")]
    [InlineData("../../../../g", "http://a/g")]
    [InlineData("/./g", "http://a/g")]
    [InlineData("/../g", "http://a/g")]
    [InlineData("g.", "http://a/b/c/g.")]
    [InlineData(".g", "http://a/b/c/.g")]
    [InlineData("g..", "http://a/b/c/g..")]
    [InlineData("..g", "http://a/b/c/..g")]
    [InlineData("./../g", "http://a/b/g")]
    [InlineData("./g/.", "http://a/b/c/g/")]
    [InlineData("g/./h", "http://a/b/c/g/h")]
    [InlineData("g/../h", "http://a/b/c/h")]
    [InlineData("g;x=1/./y", "http://a/b/c/g;x=1/y")]
    [InlineData("g;x=1/../y", "http://a/b/c/y")]
    [InlineData("g?y/./x", "http://a/b/c/g?y/./x")]
    [InlineData("g?y/../x", "http://a/b/c/g?y/../x")]
    [InlineData("g#s/./x", "http://a/b/c/g#s/./x")]
    [InlineData("g#s/../x", "http://a/b/c/g#s/../x")]
    public async Task Resolves_as_RFC_3986_section_5_4(string reference, string expected)
        => (await Resolve(reference)).Should().Be(expected);
}
