using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:function-lookup($name as xs:QName, $arity as xs:integer) as function(*)?
/// </summary>
public sealed class FunctionLookupFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "function-lookup");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [
            new() { Name = new QName(NamespaceId.None, "name"), Type = XdmSequenceType.Item },
            new() { Name = new QName(NamespaceId.None, "arity"), Type = XdmSequenceType.Integer }
        ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var name = arguments[0];
        // Validate: name must be exactly one xs:QName (not empty sequence, not multiple items)
        if (name is object?[] nameArr)
        {
            if (nameArr.Length == 0)
                throw new Execution.XQueryRuntimeException("XPTY0004",
                    "First argument to fn:function-lookup must be a single xs:QName, got empty sequence");
            if (nameArr.Length > 1)
                throw new Execution.XQueryRuntimeException("XPTY0004",
                    $"First argument to fn:function-lookup must be a single xs:QName, got sequence of length {nameArr.Length}");
            name = nameArr[0];
        }
        if (name is null)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                "First argument to fn:function-lookup must be a single xs:QName, got empty sequence");

        // Validate: arity must be exactly one xs:integer (not empty sequence)
        var arityArg = arguments[1];
        if (arityArg is object?[] arityArr)
        {
            if (arityArr.Length == 0)
                throw new Execution.XQueryRuntimeException("XPTY0004",
                    "Second argument to fn:function-lookup must be a single xs:integer, got empty sequence");
            if (arityArr.Length > 1)
                throw new Execution.XQueryRuntimeException("XPTY0004",
                    $"Second argument to fn:function-lookup must be a single xs:integer, got sequence of length {arityArr.Length}");
            arityArg = arityArr[0];
        }
        if (arityArg is null)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                "Second argument to fn:function-lookup must be a single xs:integer, got empty sequence");
        var arity = Convert.ToInt32(arityArg);

        QName qname;
        if (name is QName qn)
        {
            qname = qn;
        }
        else
        {
            // String form: try to parse as Clark notation or local name
            var nameStr = name!.ToString() ?? "";
            qname = new QName(FunctionNamespaces.Fn, nameStr);
        }

        if (context is QueryExecutionContext qec)
        {
            var func = qec.Functions.Resolve(qname, arity);
            if (func == null)
                return ValueTask.FromResult<object?>(null);
            // Per XPath 3.1 §3.1.6, if function-lookup resolves to a context-dependent
            // function (e.g., fn:static-base-uri#0, fn:name#0, fn:lang#1), the
            // dynamic context in force at the lookup call site must be captured so that
            // later invocation of the returned function uses that context — not the
            // caller's current context at the time of invocation.
            if (PhoenixmlDb.XQuery.Execution.NamedFunctionRefOperator.IsContextCaptureFunction(qname))
            {
                object? capturedItem;
                int capturedPosition = 1, capturedSize = 1;
                try
                {
                    capturedItem = qec.ContextItem;
                    capturedPosition = qec.Position;
                    capturedSize = qec.Last;
                }
                catch (Execution.XQueryRuntimeException) { capturedItem = Execution.QueryExecutionContext.AbsentFocus; }
                return ValueTask.FromResult<object?>(
                    new Execution.ContextBoundFunctionRef(func, capturedItem, qec.StaticBaseUri, capturedPosition, capturedSize));
            }
            return ValueTask.FromResult<object?>(func);
        }
        return ValueTask.FromResult<object?>(null);
    }
}
