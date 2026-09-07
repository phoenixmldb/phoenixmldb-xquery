using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:translate($arg, $mapString, $transString) as xs:string
/// </summary>
public sealed class TranslateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "translate");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "mapString"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "transString"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Type checking: all arguments must be string-like (xs:string, xs:untypedAtomic, xs:anyURI)
        ValidateStringArg(arguments[0], "arg");
        ValidateStringArgRequired(arguments[1], "mapString");
        ValidateStringArgRequired(arguments[2], "transString");

        var str = arguments[0]?.ToString() ?? "";
        var mapString = arguments[1]?.ToString() ?? "";
        var transString = arguments[2]?.ToString() ?? "";

        // Use codepoint-level operations to handle surrogate pairs / non-BMP characters
        var mapCodepoints = ToCodepoints(mapString);
        var transCodepoints = ToCodepoints(transString);

        var sb = new System.Text.StringBuilder(str.Length);
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(str);
        while (enumerator.MoveNext())
        {
            var textElement = enumerator.GetTextElement();
            var codepoint = char.ConvertToUtf32(textElement, 0);
            var index = mapCodepoints.IndexOf(codepoint);
            if (index < 0)
            {
                sb.Append(textElement);
            }
            else if (index < transCodepoints.Count)
            {
                sb.Append(char.ConvertFromUtf32(transCodepoints[index]));
            }
            // else: character is deleted
        }

        return ValueTask.FromResult<object?>(sb.ToString());
    }

    private static void ValidateStringArg(object? arg, string paramName, Ast.ExecutionContext? context = null)
    {
        if (arg == null) return;
        // Allow nodes (they get atomized to string) and string-like types
        if (arg is Xdm.Nodes.XdmNode) return;
        if (arg is string) return;
        if (arg is Xdm.XsUntypedAtomic) return;
        if (arg is Xdm.XsAnyUri) return;
        throw context.Error("XPTY0004",
            $"fn:translate: argument ${paramName} must be xs:string, got {arg.GetType().Name}");
    }

    private static void ValidateStringArgRequired(object? arg, string paramName, Ast.ExecutionContext? context = null)
    {
        if (arg == null)
            throw context.Error("XPTY0004",
                $"fn:translate: argument ${paramName} cannot be an empty sequence");
        ValidateStringArg(arg, paramName);
    }

    private static List<int> ToCodepoints(string s, Ast.ExecutionContext? context = null)
    {
        var result = new List<int>();
        for (int i = 0; i < s.Length; i++)
        {
            int cp;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }
            else
            {
                cp = s[i];
            }
            result.Add(cp);
        }
        return result;
    }
}
