using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:implicit-timezone() as xs:dayTimeDuration</summary>
public sealed class ImplicitTimezoneFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "implicit-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        return ValueTask.FromResult<object?>((object)DateTimeOffset.Now.Offset);
    }
}
