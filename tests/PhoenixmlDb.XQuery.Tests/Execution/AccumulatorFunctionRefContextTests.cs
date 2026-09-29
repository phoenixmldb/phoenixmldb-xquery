using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// fn:accumulator-before and fn:accumulator-after (registered by the XSLT engine) read the
/// context node, so a named reference to them binds the focus where it is created, like
/// fn:name#0 (XPath 3.1 §3.1.6). W3C XSLT accumulator-062 binds <c>../accumulator-before#1</c>
/// in a template and calls it later; without the capture the call saw no context item.
/// </summary>
public class AccumulatorFunctionRefContextTests
{
    /// <summary>Stands in for the XSLT function: returns the name of its context node.</summary>
    private sealed class ContextNameProbe(string localName) : XQueryFunction
    {
        public override QName Name => new(FunctionNamespaces.Fn, localName);
        public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
        public override IReadOnlyList<FunctionParameterDef> Parameters =>
            [new() { Name = new QName(NamespaceId.None, "name"), Type = XdmSequenceType.OptionalItem }];

        public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, PhoenixmlDb.XQuery.Ast.ExecutionContext context) =>
            ValueTask.FromResult<object?>((context.ContextItem as Xdm.Nodes.XdmElement)?.LocalName);
    }

    [Theory]
    [InlineData("accumulator-before")]
    [InlineData("accumulator-after")]
    public async Task Named_reference_binds_the_focus_where_it_is_created(string function)
    {
        var library = FunctionLibrary.Standard.Copy();
        library.Register(new ContextNameProbe(function));
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, functions: library);

        var compiled = engine.Compile(
            $"let $doc := document {{ <outer><inner/></outer> }} " +
            $"let $f := $doc/outer/inner/../{function}#1 " +
            "return $doc/outer/inner/$f('x')");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));

        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        items.Should().Equal("outer");
    }
}
