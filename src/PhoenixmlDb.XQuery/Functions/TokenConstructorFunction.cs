using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:token($arg)</summary>
public sealed class TokenConstructorFunction : TypeConstructorFunction
{
    public TokenConstructorFunction() : base("token") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // XSD xs:token whitespace facet = "collapse": only #x9, #xA, #xD, #x20 are whitespace.
        // Do NOT treat Unicode whitespace (NBSP \xa0, en-space, etc.) as whitespace — string.Trim()
        // would incorrectly remove them (see QT3 CastAs678).
        return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsTypedString(CollapseXmlWhitespace(arg.ToString() ?? ""), "token"));
    }

    internal static string CollapseXmlWhitespace(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        bool pendingSpace = false;
        bool seenNonWs = false;
        foreach (var c in input)
        {
            bool isXmlWs = c == ' ' || c == '\t' || c == '\n' || c == '\r';
            if (isXmlWs)
            {
                if (seenNonWs) pendingSpace = true;
            }
            else
            {
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                sb.Append(c);
                seenNonWs = true;
            }
        }
        return sb.ToString();
    }
}
