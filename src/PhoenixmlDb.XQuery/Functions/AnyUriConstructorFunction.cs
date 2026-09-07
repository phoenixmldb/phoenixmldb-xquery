using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:anyURI($arg)</summary>
public sealed class AnyUriConstructorFunction : TypeConstructorFunction
{
    public AnyUriConstructorFunction() : base("anyURI") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // XSD xs:anyURI whitespace facet = "collapse" (per XSD built-in datatypes spec):
        // trim leading/trailing XML whitespace and collapse internal runs to a single space.
        // Only #x9, #xA, #xD, #x20 count — Unicode whitespace (NBSP, etc.) is preserved.
        var s = TokenConstructorFunction.CollapseXmlWhitespace(arg.ToString() ?? "");
        return ValueTask.FromResult<object?>(new Xdm.XsAnyUri(s));
    }
}
