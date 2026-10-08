using FluentAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// fn:collection and fn:uri-collection on a file location, called directly and through
/// fn:function-lookup, under a policy that allows no file and under a resolver that is the
/// only source of resources: the file is not read.
/// </summary>
public sealed class CollectionOfFilesPolicyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-coll").FullName;
    private readonly string _file;

    public CollectionOfFilesPolicyTests()
    {
        File.WriteAllText(Path.Combine(_dir, "a.xml"), "<a>on-disk</a>");
        _file = new Uri(Path.Combine(_dir, "a.xml")).AbsoluteUri;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Nothing : ResourceResolverBase
    {
        public override bool SuppliesAllContent => true;
    }

    [Theory]
    [InlineData("string(collection('{0}'))", false)]
    [InlineData("string(function-lookup(xs:QName('fn:collection'), 1)('{0}'))", false)]
    [InlineData("string(function-lookup(xs:QName('fn:collection'), 1)('{1}'))", false)]
    [InlineData("string-join(function-lookup(xs:QName('fn:uri-collection'), 1)('{1}') ! string(), ' ')", false)]
    [InlineData("string(collection('{0}'))", true)]
    [InlineData("string(function-lookup(xs:QName('fn:collection'), 1)('{0}'))", true)]
    [InlineData("string(function-lookup(xs:QName('fn:collection'), 1)('{1}'))", true)]
    [InlineData("string-join(function-lookup(xs:QName('fn:uri-collection'), 1)('{1}') ! string(), ' ')", true)]
    public async Task A_file_is_not_read_as_a_collection(string query, bool resolver)
    {
        var builder = ResourcePolicy.CreateBuilder();
        var policy = (resolver ? builder.WithResourceResolver(new Nothing()) : builder).Build();
        var text = "try { " + string.Format(System.Globalization.CultureInfo.InvariantCulture, query, _file, new Uri(_dir).AbsoluteUri)
                            + " } catch * { 'refused' }";

        var result = await new XQueryFacade { ResourcePolicy = policy }.EvaluateAsync(text);

        result.Should().NotContain("on-disk").And.NotContain("a.xml");
    }
}
