using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:error($code, $description) as none
/// </summary>
public sealed class ErrorFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "error");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.Zero };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "code"), Type = new() { ItemType = ItemType.QName, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "description"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var code = arguments[0];
        var description = arguments[1]?.ToString() ?? "Error raised by fn:error";

        var errorCode = code?.ToString() ?? "FOER0000";
        throw new XQueryException(errorCode, description);
    }
}

/// <summary>
/// fn:error($code as xs:QName?) as none
/// </summary>
public sealed class Error1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "error");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.Zero };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "code"), Type = new() { ItemType = ItemType.QName, Occurrence = Occurrence.ZeroOrOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var code = arguments[0];
        var errorCode = code?.ToString() ?? "FOER0000";
        throw new XQueryException(errorCode, $"Error raised by fn:error: {errorCode}");
    }
}

/// <summary>
/// fn:error() as none
/// </summary>
public sealed class Error0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "error");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.Zero };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        throw new XQueryException("FOER0000", "Error raised by fn:error");
    }
}

/// <summary>
/// fn:trace($value, $label) as item()*
/// </summary>
public sealed class TraceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "trace");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "label"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var value = arguments[0];
        var label = arguments[1]?.ToString() ?? "";

        // In a real implementation, this would write to a trace output
        // For now, just write to debug output
        System.Diagnostics.Debug.WriteLine($"[TRACE] {label}: {value}");

        // fn:trace returns its first argument unchanged
        return ValueTask.FromResult(value);
    }
}

/// <summary>
/// Exception thrown by XQuery error function or during query execution.
/// </summary>
public class XQueryException : Exception
{
    public string ErrorCode { get; }

    public XQueryException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public XQueryException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
