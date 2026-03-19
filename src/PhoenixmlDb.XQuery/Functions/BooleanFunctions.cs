using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:not($arg) as xs:boolean
/// </summary>
public sealed class NotFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "not");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var ebv = Execution.QueryExecutionContext.EffectiveBooleanValue(arguments[0]);
        return ValueTask.FromResult<object?>(!ebv);
    }
}

/// <summary>
/// fn:true() as xs:boolean
/// </summary>
public sealed class TrueFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "true");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return ValueTask.FromResult<object?>(true);
    }
}

/// <summary>
/// fn:false() as xs:boolean
/// </summary>
public sealed class FalseFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "false");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return ValueTask.FromResult<object?>(false);
    }
}

/// <summary>
/// fn:boolean($arg) as xs:boolean
/// </summary>
public sealed class BooleanFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "boolean");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var value = arguments[0];
        return ValueTask.FromResult<object?>(Execution.QueryExecutionContext.EffectiveBooleanValue(value));
    }
}
