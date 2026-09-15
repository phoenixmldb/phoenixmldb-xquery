using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// copy-namespaces no-inherit is implemented with an internal marker binding on the copy root. The
/// marker is not a namespace: it must never reach serialized output or user-visible namespace
/// functions. It did — direct serialization threw "Invalid name character" and fn:serialize emitted
/// an attribute whose name is not XML, silently.
/// </summary>
public class NoInheritMarkerLeakTests
{
    private const string Doc = "<book xmlns:p=\"urn:q\"><e><p:d/></e></book>";
    private const string NoInherit = "declare copy-namespaces preserve, no-inherit; ";
    private static Task<string> Eval(string q) => new XQueryFacade().EvaluateAsync(q, Doc);

    [Fact]
    public async Task Leaf_copy_serializes()
        => (await Eval(NoInherit + "<ctor>{/*:book/*:e/*:d}</ctor>"))
            .Should().Be("<ctor><p:d xmlns:p=\"urn:q\"/></ctor>");

    [Fact]
    public async Task Subtree_copy_serializes()
        => (await Eval(NoInherit + "<ctor xmlns:z=\"urn:z\">{/*:book/*:e}</ctor>"))
            .Should().Be("<ctor xmlns:z=\"urn:z\"><e xmlns:p=\"urn:q\"><p:d/></e></ctor>");

    [Fact]
    public async Task Fn_serialize_output_is_well_formed_and_round_trips()
        => (await Eval(NoInherit + "string-join(parse-xml(serialize(<ctor>{/*:book/*:e}</ctor>))//*:d ! namespace-uri(.), ',')"))
            .Should().Be("urn:q");

    [Fact]
    public async Task The_marker_is_not_an_in_scope_prefix()
        => (await Eval(NoInherit + "string-join(sort(in-scope-prefixes(<ctor>{/*:book/*:e}</ctor>/*:e)), ' ')"))
            .Should().Be("p xml");

    [Fact]
    public async Task No_inherit_still_hides_the_enclosing_constructors_namespaces()
        => (await Eval(NoInherit + "string-join(sort(in-scope-prefixes(<ctor xmlns:z=\"urn:z\">{/*:book/*:e}</ctor>/*:e)), ' ')"))
            .Should().NotContain("z", "no-inherit: the copy must not see the enclosing constructor's z binding");
}
