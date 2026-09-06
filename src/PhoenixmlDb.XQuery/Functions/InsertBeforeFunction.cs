using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:insert-before($target, $position, $inserts) as item()*
/// </summary>
public sealed class InsertBeforeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "insert-before");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "target"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "position"), Type = XdmSequenceType.Integer },
        new() { Name = new QName(NamespaceId.None, "inserts"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var target = arguments[0];
        var posArg = arguments[1];
        // XPTY0004: position must be xs:integer
        if (posArg == null || posArg is double or float or decimal or string or Xdm.XsAnyUri)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:insert-before: position must be xs:integer, got {posArg?.GetType().Name ?? "empty sequence"}");
        var position = QueryExecutionContext.ToInt(posArg);
        var inserts = arguments[2];

        var targetArr = target is object?[] ta ? ta : target is IEnumerable<object?> t ? t.ToArray() : target != null ? [target] : Array.Empty<object?>();
        var insertsArr = inserts is object?[] ia ? ia : inserts is IEnumerable<object?> i ? i.ToArray() : inserts != null ? [inserts] : Array.Empty<object?>();

        // XPath uses 1-based indexing
        var insertIndex = position - 1;
        if (insertIndex < 0) insertIndex = 0;
        if (insertIndex > targetArr.Length) insertIndex = targetArr.Length;

        var result = new object?[targetArr.Length + insertsArr.Length];
        Array.Copy(targetArr, 0, result, 0, insertIndex);
        Array.Copy(insertsArr, 0, result, insertIndex, insertsArr.Length);
        Array.Copy(targetArr, insertIndex, result, insertIndex + insertsArr.Length, targetArr.Length - insertIndex);
        return ValueTask.FromResult<object?>(result);
    }
}
