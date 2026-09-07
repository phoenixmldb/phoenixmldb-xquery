using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:base64Binary($arg)</summary>
public sealed class Base64BinaryConstructorFunction : TypeConstructorFunction
{
    public Base64BinaryConstructorFunction() : base("base64Binary") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Handle XdmValue binary inputs (cast from hexBinary to base64Binary or identity)
        if (arg is XdmValue xv && xv.RawValue is byte[] xvBytes)
            return ValueTask.FromResult<object?>(XdmValue.Base64Binary(xvBytes));
        if (arg is byte[] bytes) return ValueTask.FromResult<object?>(XdmValue.Base64Binary(bytes));
        var b64 = arg.ToString()!.Trim();
        // XSD base64Binary grammar requires canonical padding: the char preceding "==" must be
        // one of [AQgw] (encodes only 2 bits + 4 padding zeros), and the char preceding a lone "="
        // must be one of [AEIMQUYcgkosw048] (encodes 4 bits + 2 padding zeros). .NET's
        // Convert.FromBase64String doesn't enforce this — validate manually (QT3 K-SeqExprCast-129).
        var compact = new System.Text.StringBuilder(b64.Length);
        foreach (var c in b64) if (c != ' ' && c != '\t' && c != '\n' && c != '\r') compact.Append(c);
        var c64 = compact.ToString();
        if (c64.EndsWith("==", StringComparison.Ordinal))
        {
            if (c64.Length < 4 || "AQgw".IndexOf(c64[^3]) < 0)
                throw new InvalidOperationException($"FORG0001: Invalid value for xs:base64Binary: '{b64}'");
        }
        else if (c64.EndsWith('='))
        {
            if (c64.Length < 4 || "AEIMQUYcgkosw048".IndexOf(c64[^2]) < 0)
                throw new InvalidOperationException($"FORG0001: Invalid value for xs:base64Binary: '{b64}'");
        }
        try
        {
            var data = Convert.FromBase64String(b64);
            return ValueTask.FromResult<object?>(XdmValue.Base64Binary(data));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"FORG0001: Invalid value for xs:base64Binary: '{b64}'");
        }
    }
}
