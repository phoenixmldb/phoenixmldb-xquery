using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:IDREF($arg) -- xs:IDREF is derived from xs:NCName</summary>
public sealed class IDRefConstructorFunction : TypeConstructorFunction
{
    public IDRefConstructorFunction() : base("IDREF") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        RequireStringOrUntyped(arg, "xs:IDREF");
        var s = NormalizeWhitespace(AtomicToString(arg));
        if (s.Length == 0)
            throw new XQueryRuntimeException("FORG0001", "Empty string is not a valid xs:IDREF");
        try { XmlConvert.VerifyNCName(s); }
        catch { throw new XQueryRuntimeException("FORG0001", $"Invalid xs:IDREF: '{s}'"); }
        return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsTypedString(s, "IDREF"));
    }
}
