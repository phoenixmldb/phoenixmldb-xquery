using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:codepoint-equal($comparand1, $comparand2) as xs:boolean?
/// Returns true if the two arguments are equal using Unicode codepoint comparison.
/// </summary>
public sealed class CodepointEqualFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "codepoint-equal");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Boolean, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "comparand1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "comparand2"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg1 = arguments[0];
        var arg2 = arguments[1];
        // XPTY0004: arguments must be strings (or empty sequence/untypedAtomic)
        var s1 = StringArgument.AsString(arg1);
        var s2 = StringArgument.AsString(arg2);
        if (arg1 != null && s1 is null)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"First argument to fn:codepoint-equal must be xs:string, got {arg1.GetType().Name}");
        if (arg2 != null && s2 is null)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"Second argument to fn:codepoint-equal must be xs:string, got {arg2.GetType().Name}");
        // If either argument is empty sequence, return empty sequence
        if (s1 is null || s2 is null)
            return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(string.Equals(s1, s2, StringComparison.Ordinal));
    }
}
