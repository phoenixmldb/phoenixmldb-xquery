using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

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
        //
        // Render through XdmShape rather than interpolating the value. Interpolation calls
        // ToString(), which on a container gives the CLR type name: a sequence printed as
        // "System.Object[]" and an array as "System.Collections.Generic.List`1[System.Object]".
        // A function whose whole purpose is letting someone inspect a value must not answer with
        // the name of the box it arrived in.
        var rendered = XdmShape.Render(value);
        var message = string.IsNullOrEmpty(label) ? rendered : $"{label}: {rendered}";
        await Console.Error.WriteLineAsync($"[fn:trace] {message}").ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[TRACE] {message}");

        // fn:trace returns its first argument unchanged
        return value;
    }
}
