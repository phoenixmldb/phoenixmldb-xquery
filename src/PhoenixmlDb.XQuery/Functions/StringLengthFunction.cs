using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:string-length($arg as xs:string?) as xs:integer
/// </summary>
public sealed class StringLengthFunction : XQueryFunction
{
    /// <summary>
    /// Enforces XPTY0004 when the atomized argument isn't xs:string, xs:anyURI,
    /// or xs:untypedAtomic. Numeric, boolean, date/time types are rejected.
    /// </summary>
    internal static void RequireStringLike(object? atomized, string fnName, Ast.ExecutionContext? context = null)
    {
        if (atomized is null) return;
        if (atomized is string) return;
        if (atomized is Xdm.XsUntypedAtomic) return;
        if (atomized is Xdm.XsAnyUri) return;
        if (atomized is Xdm.XsTypedString) return;
        throw new Execution.XQueryRuntimeException("XPTY0004",
            $"fn:{fnName} expects xs:string?, got {atomized.GetType().Name}");
    }

    public override QName Name => new(FunctionNamespaces.Fn, "string-length");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var atomized = Execution.QueryExecutionContext.Atomize(arguments.Count > 0 ? arguments[0] : null, nodeProvider);
        if (atomized is null) return ValueTask.FromResult<object?>((long)0);
        RequireStringLike(atomized, "string-length");
        var arg = ConcatFunction.XQueryStringValue(atomized);
        return ValueTask.FromResult<object?>((long)CountCodepoints(arg));
    }

    /// <summary>
    /// Count Unicode codepoints (scalar values) in a string. This differs from
    /// StringInfo.LengthInTextElements which counts grapheme clusters (merging
    /// CR+LF into one element), and from String.Length which counts UTF-16 code units.
    /// XQuery defines string-length as the number of codepoints.
    /// </summary>
    internal static int CountCodepoints(string s, Ast.ExecutionContext? context = null)
    {
        int count = 0;
        for (int i = 0; i < s.Length; i++)
        {
            count++;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                i++; // surrogate pair = one codepoint, skip low surrogate
        }
        return count;
    }
}
