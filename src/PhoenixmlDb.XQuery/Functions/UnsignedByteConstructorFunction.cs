using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:unsignedByte($arg)</summary>
public sealed class UnsignedByteConstructorFunction : TypeConstructorFunction
{
    public UnsignedByteConstructorFunction() : base("unsignedByte") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var result = arg switch
        {
            long l => EnsureRange(l, byte.MinValue, byte.MaxValue),
            int i => EnsureRange(i, byte.MinValue, byte.MaxValue),
            string s => (long)byte.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => (long)Convert.ToByte(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(new Xdm.XsTypedInteger(result, "unsignedByte"));
    }

    private static long EnsureRange(long val, long min, long max) =>
        val >= min && val <= max ? val : throw new XQueryRuntimeException("FORG0001", $"Value {val} out of range for xs:unsignedByte");
}
