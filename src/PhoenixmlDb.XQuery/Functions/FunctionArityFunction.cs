using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:function-arity($func as function(*)) as xs:integer
/// </summary>
public sealed class FunctionArityFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "function-arity");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "func"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is XQueryFunction func)
        {
            return ValueTask.FromResult<object?>((long)func.Arity);
        }
        // Maps and arrays are callable as functions — arity is 1
        if (arguments[0] is IDictionary<object, object?> || arguments[0] is List<object?>)
            return ValueTask.FromResult<object?>(1L);
        throw new XQueryRuntimeException("XPTY0004", "Argument is not a function");
    }
}
