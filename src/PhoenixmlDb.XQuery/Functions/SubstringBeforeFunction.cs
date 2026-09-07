using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:substring-before($arg1, $arg2) as xs:string
/// </summary>
public sealed class SubstringBeforeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "substring-before");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var str = ConcatFunction.XQueryStringValue(arguments[0], nodeProvider);
        var search = ConcatFunction.XQueryStringValue(arguments[1], nodeProvider);
        if (string.IsNullOrEmpty(search))
            return ValueTask.FromResult<object?>("");
        var comparison = CollationHelper.GetDefaultComparison(context);
        var idx = str.IndexOf(search, comparison);
        return ValueTask.FromResult<object?>(idx < 0 ? "" : str[..idx]);
    }
}
