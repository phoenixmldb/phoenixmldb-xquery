using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:unsignedLong($arg)</summary>
public sealed class UnsignedLongConstructorFunction : TypeConstructorFunction
{
    public UnsignedLongConstructorFunction() : base("unsignedLong") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Return as long for values that fit, BigInteger for values > long.MaxValue
        object? result = arg switch
        {
            long l when l >= 0 => l,
            long l => throw new XQueryRuntimeException("FORG0001", $"Value {l} out of range for xs:unsignedLong"),
            int i when i >= 0 => (long)i,
            int i => throw new XQueryRuntimeException("FORG0001", $"Value {i} out of range for xs:unsignedLong"),
            System.Numerics.BigInteger bi when bi >= 0 && bi <= ulong.MaxValue =>
                bi <= long.MaxValue ? (object)(long)bi : bi,
            System.Numerics.BigInteger bi => throw new XQueryRuntimeException("FORG0001", $"Value {bi} out of range for xs:unsignedLong"),
            string s => ParseUnsignedLong(s),
            _ => Convert.ToInt64(arg, CultureInfo.InvariantCulture)
        };
        // Tag the result with the declared XSD type: a value that fits in a long as an
        // XsTypedInteger, a larger one (up to 2^64-1) with the type kept beside the value.
        // A larger one was left a bare xs:integer, and was not an instance of xs:unsignedLong.
        if (result is long ul)
            return ValueTask.FromResult<object?>(new Xdm.XsTypedInteger(ul, "unsignedLong"));
        if (result is System.Numerics.BigInteger big)
            return ValueTask.FromResult<object?>(Execution.TypeCastHelper.TagBigDerivedInteger(big, "unsignedLong"));
        return ValueTask.FromResult<object?>(result);
    }

    private static object ParseUnsignedLong(string s)
    {
        var v = ulong.Parse(s.Trim(), CultureInfo.InvariantCulture);
        return v <= (ulong)long.MaxValue ? (object)(long)v : new System.Numerics.BigInteger(v);
    }
}
