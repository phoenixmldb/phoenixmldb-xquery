using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:environment-variable($name) as xs:string?
/// </summary>
public sealed class EnvironmentVariableFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "environment-variable");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "name"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null || (arg is object?[] arr && arr.Length == 0))
            throw context.Error("XPTY0004", "fn:environment-variable() requires xs:string, got empty sequence");
        if (arg is not string && arg is not PhoenixmlDb.Xdm.XsUntypedAtomic)
            throw context.Error("XPTY0004", $"fn:environment-variable() requires xs:string, got {arg.GetType().Name}");
        // For security, return empty sequence (no environment variable access)
        return ValueTask.FromResult<object?>(null);
    }
}
