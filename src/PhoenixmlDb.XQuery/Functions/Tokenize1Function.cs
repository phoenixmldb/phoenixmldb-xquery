using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:tokenize($input) as xs:string* — 1-arg tokenize splits on whitespace
/// </summary>
public sealed class Tokenize1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "tokenize");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Per XPath 3.1 §5.4.3.2: 1-arg tokenize splits on XML whitespace only
        // (U+0020, U+0009, U+000A, U+000D), NOT Unicode whitespace like NBSP.
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var raw = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0], nodeProvider));
        var input = raw.Trim(' ', '\t', '\n', '\r');
        if (string.IsNullOrEmpty(input))
            return ValueTask.FromResult<object?>(Array.Empty<string>());

        var tokens = System.Text.RegularExpressions.Regex.Split(input, @"[ \t\n\r]+");
        return ValueTask.FromResult<object?>(tokens);
    }
}
