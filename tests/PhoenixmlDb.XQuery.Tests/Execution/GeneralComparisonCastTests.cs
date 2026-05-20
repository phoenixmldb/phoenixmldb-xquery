using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// QT3 GenCompEq-22 + adjacent: general comparison <c>=</c> with an
/// <c>xs:untypedAtomic</c> operand and an <c>xs:QName</c> operand must cast
/// the untyped value to xs:QName using the in-scope namespace bindings — not
/// a synthesized hash-derived NamespaceId, which never matched anything.
/// </summary>
public class GeneralComparisonCastTests
{
    [Fact]
    public async System.Threading.Tasks.Task UntypedAtomic_eq_QName_castsUsingInScopeNamespaces()
    {
        // The QT3 test verbatim.
        var query = """
            declare namespace z = "http://example.com/z";
            declare variable $p external := xs:untypedAtomic('z:local');
            $p = (<xs:element/>, <z:local/>, <fn:function/>)!node-name(.)
            """;

        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(
            string.Join("; ", compiled.Errors.Select(e => e.Message)));

        using var ctx = engine.CreateContext();
        var items = new System.Collections.Generic.List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            items.Add(item);

        items.Should().ContainSingle().Which.Should().Be(true,
            "xs:untypedAtomic('z:local') cast to xs:QName must use the prolog z= binding to match z:local in the sequence");
    }

    [Fact]
    public async System.Threading.Tasks.Task UntypedAtomic_eq_QName_returnsFalseForUnmatched()
    {
        var query = """
            declare namespace a = "http://example.com/a";
            declare namespace b = "http://example.com/b";
            declare variable $p external := xs:untypedAtomic('a:foo');
            $p = (<b:foo/>)!node-name(.)
            """;

        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue();

        using var ctx = engine.CreateContext();
        var items = new System.Collections.Generic.List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            items.Add(item);

        items.Should().ContainSingle().Which.Should().Be(false,
            "a:foo (URL a) and b:foo (URL b) are distinct QNames even though local-names match");
    }

    [Fact]
    public async System.Threading.Tasks.Task UntypedAtomic_eq_QName_undeclaredPrefixRaisesFONS0004()
    {
        var query = """
            declare variable $p external := xs:untypedAtomic('undeclared:local');
            $p = (<a/>)!node-name(.)
            """;

        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue();

        using var ctx = engine.CreateContext();
        var act = async () =>
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(ctx)) { }
        };

        var ex = await act.Should().ThrowAsync<XQueryRuntimeException>();
        ex.Which.ErrorCode.Should().Be("FONS0004");
    }
}
