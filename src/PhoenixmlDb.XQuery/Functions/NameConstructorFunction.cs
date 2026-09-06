using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:Name($arg)</summary>
public sealed class NameConstructorFunction : TypeConstructorFunction
{
    public NameConstructorFunction() : base("Name") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        RequireStringOrUntyped(arg, "xs:Name");
        var s = NormalizeWhitespace(AtomicToString(arg));
        if (s.Length == 0)
            throw new XQueryRuntimeException("FORG0001", "Empty string is not a valid xs:Name");
        // Validate XML Name: starts with NameStartChar, followed by NameChars
        try { XmlConvert.VerifyName(s); }
        catch { throw new XQueryRuntimeException("FORG0001", $"Invalid xs:Name: '{s}'"); }
        return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsTypedString(s, "Name"));
    }
}
