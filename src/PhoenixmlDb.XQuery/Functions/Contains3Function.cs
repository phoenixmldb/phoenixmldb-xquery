using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:contains($arg1, $arg2, $collation) as xs:boolean
/// </summary>
public sealed class Contains3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "contains");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var str = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0], nodeProvider));
        var search = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[1], nodeProvider));
        var collationUri = CollationHelper.ResolveCollationUri(arguments[2]?.ToString(), context);
        if (CollationHelper.IsHtmlAsciiCaseInsensitive(collationUri))
            return ValueTask.FromResult<object?>(CollationHelper.AsciiLower(str).Contains(CollationHelper.AsciiLower(search), StringComparison.Ordinal));
        if (CollationHelper.IsUca(collationUri))
            return ValueTask.FromResult<object?>(CollationHelper.UcaContains(str, search, collationUri!));
        var comparison = CollationHelper.GetStringComparison(collationUri);
        return ValueTask.FromResult<object?>(str.Contains(search, comparison));
    }
}
