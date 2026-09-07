using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:ENTITY($arg)</summary>
public sealed class EntityConstructorFunction : TypeConstructorFunction
{
    public EntityConstructorFunction() : base("ENTITY") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        RequireStringOrUntyped(arg, "xs:ENTITY");
        var s = NormalizeWhitespace(AtomicToString(arg));
        if (s.Length == 0)
            throw new XQueryRuntimeException("FORG0001", "Empty string is not a valid xs:ENTITY");
        // ENTITY must be a valid NCName
        try { XmlConvert.VerifyNCName(s); }
        catch { throw new XQueryRuntimeException("FORG0001", $"Invalid xs:ENTITY: '{s}'"); }
        return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsTypedString(s, "ENTITY"));
    }
}
