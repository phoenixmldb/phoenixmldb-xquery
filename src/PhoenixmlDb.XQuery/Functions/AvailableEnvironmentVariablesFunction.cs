using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:available-environment-variables() as xs:string*
/// </summary>
public sealed class AvailableEnvironmentVariablesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "available-environment-variables");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // For security, return empty sequence
        return ValueTask.FromResult<object?>(Array.Empty<string>());
    }

}
