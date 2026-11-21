using AwesomeAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// Function names and node names use one namespace id space: each well-known function namespace
/// id is Core's reserved id for the same URI.
/// </summary>
/// <remarks>
/// The function library gave the XML Schema namespace 2 and the local-functions namespace 4,
/// which are Core's ids for xmlns and xsi. Code that resolved a function name's id through a
/// node store, or a node's id through the function table, read the wrong URI.
/// </remarks>
public class FunctionNamespaceIdTests
{
    [Fact]
    public void Every_well_known_function_namespace_id_is_Cores_id_for_its_uri()
    {
        foreach (var (uri, id) in FunctionNamespaces.WellKnown)
            NamespaceRegistry.GetUri(id).Should().Be(uri, $"the id {id.Value} of {uri}");
    }

    [Fact]
    public void Xs_and_local_have_their_reserved_ids()
    {
        FunctionNamespaces.Xs.Should().Be(NamespaceId.Xsd);
        FunctionNamespaces.Local.Should().Be(NamespaceId.XQueryLocal);
    }
}
