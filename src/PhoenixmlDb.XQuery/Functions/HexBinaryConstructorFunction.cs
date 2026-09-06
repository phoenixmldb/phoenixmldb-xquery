using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:hexBinary($arg)</summary>
public sealed class HexBinaryConstructorFunction : TypeConstructorFunction
{
    public HexBinaryConstructorFunction() : base("hexBinary") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Handle XdmValue binary inputs (cast from base64Binary to hexBinary or identity)
        if (arg is XdmValue xv && xv.RawValue is byte[] xvBytes)
            return ValueTask.FromResult<object?>(XdmValue.HexBinary(xvBytes));
        if (arg is byte[] bytes) return ValueTask.FromResult<object?>(XdmValue.HexBinary(bytes));
        var hex = arg.ToString()!.Trim();
        try
        {
            var data = Convert.FromHexString(hex);
            return ValueTask.FromResult<object?>(XdmValue.HexBinary(data));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"FORG0001: Invalid value for xs:hexBinary: '{hex}'");
        }
    }
}
