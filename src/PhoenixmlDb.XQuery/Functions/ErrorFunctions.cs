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
/// fn:trace($value) as item()*
/// fn:trace($value, $label) as item()*
/// </summary>
public sealed class TraceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "trace");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override bool IsVariadic => true;
    public override int MaxArity => 2;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var value = arguments[0];
        var label = arguments.Count > 1 ? arguments[1]?.ToString() ?? "" : "";

        // Per XPath spec, fn:trace writes to implementation-defined trace output.
        // We write to stderr (visible in CLI tools) and Debug (visible in debugger).
        var message = string.IsNullOrEmpty(label) ? $"{value}" : $"{label}: {value}";
        await Console.Error.WriteLineAsync($"[fn:trace] {message}").ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[TRACE] {message}");

        // fn:trace returns its first argument unchanged
        return value;
    }
}

/// <summary>
/// Exception thrown by the XQuery <c>fn:error()</c> function or when a built-in function encounters
/// an error condition defined by the XQuery and XPath Functions and Operators specification.
/// </summary>
/// <remarks>
/// <para>
/// This is the base exception type for errors raised by XQuery built-in functions (e.g., type
/// conversion failures, invalid arguments). It is distinct from <see cref="Execution.XQueryRuntimeException"/>,
/// which covers dynamic errors raised by the execution engine itself (e.g., unbound variables, context errors).
/// </para>
/// <para>
/// The <see cref="ErrorCode"/> follows the standard XQuery error code format (e.g., <c>"FOTY0013"</c>
/// for atomization of function items, <c>"FOER0000"</c> for <c>fn:error()</c> with no arguments).
/// </para>
/// </remarks>
/// <seealso cref="Execution.XQueryRuntimeException"/>
/// <seealso cref="Parser.XQueryParseException"/>
public class XQueryException : Exception
{
    /// <summary>
    /// The XQuery error code (e.g., <c>"FOTY0013"</c>, <c>"FOER0000"</c>) identifying the error
    /// as defined by the XQuery and XPath Functions and Operators specification.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Creates a new <see cref="XQueryException"/> with the specified error code and message.
    /// </summary>
    /// <param name="errorCode">A standard XQuery error code (e.g., <c>"FOTY0013"</c>).</param>
    /// <param name="message">A human-readable description of the error.</param>
    public XQueryException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Creates a new <see cref="XQueryException"/> with the specified error code, message, and inner exception.
    /// </summary>
    /// <param name="errorCode">A standard XQuery error code.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public XQueryException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
