using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:compare($string1, $string2) as xs:integer?
/// </summary>
public sealed class CompareFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "compare");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "comparand1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "comparand2"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var a0 = Execution.QueryExecutionContext.Atomize(arguments[0], nodeProvider);
        var a1 = Execution.QueryExecutionContext.Atomize(arguments[1], nodeProvider);
        if (a0 == null || a1 == null)
            return ValueTask.FromResult<object?>(null);
        // fn:compare requires xs:string arguments (xs:untypedAtomic/xs:anyURI promote to string)
        StringLengthFunction.RequireStringLike(a0, "compare");
        StringLengthFunction.RequireStringLike(a1, "compare");
        var s1 = ConcatFunction.XQueryStringValue(a0);
        var s2 = ConcatFunction.XQueryStringValue(a1);
        // Use the default collation URI directly so a UCA default collation keeps full
        // CompareInfo fidelity (e.g. de;strength=primary makes 'ss' eq 'ß'); the reduced
        // StringComparison enum cannot express that.
        var collationUri = CollationHelper.GetDefaultCollationUri(context);
        var cmp = CollationHelper.CompareWithCollation(s1, s2, collationUri, context);
        return ValueTask.FromResult<object?>((long)Math.Sign(cmp));
    }
}
