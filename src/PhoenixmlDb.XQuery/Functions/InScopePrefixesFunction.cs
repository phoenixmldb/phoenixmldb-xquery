using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:in-scope-prefixes($element) as xs:string*
/// Returns the in-scope namespace prefixes for an element.
/// </summary>
public sealed class InScopePrefixesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "in-scope-prefixes");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "element"), Type = new XdmSequenceType { ItemType = ItemType.Element, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is not XdmElement elem)
            throw context.Error("XPTY0004", $"fn:in-scope-prefixes() requires an element node, got {arguments[0]?.GetType().Name ?? "empty sequence"}");

        var prefixes = new HashSet<string>();
        // xml prefix is always in scope
        prefixes.Add("xml");

        // Gather the in-scope namespace bindings through the SAME shared routine the
        // namespace:: axis uses, resolving ancestors through the main node store, so the two
        // can never disagree (constructed elements use their complete own declarations; parsed
        // elements walk ancestors honouring xmlns="" undeclarations and the no-inherit marker).
        var nodeStore = context.NodeStore;
        foreach (var (prefix, _) in Execution.AxisNavigationOperator.GatherInScopeNamespaces(
            elem, id => nodeStore?.GetNode(id) as XdmNode))
        {
            prefixes.Add(prefix);
        }

        return ValueTask.FromResult<object?>(prefixes.Cast<object?>().ToArray());
    }
}

// ─── fn:path ───────────────────────────────────────────────────────────────
