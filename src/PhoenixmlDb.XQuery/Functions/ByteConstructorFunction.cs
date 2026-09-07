using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:byte($arg)</summary>
public sealed class ByteConstructorFunction : TypeConstructorFunction
{
    public ByteConstructorFunction() : base("byte") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // xs:byte is signed (-128 to 127) in XSD
        var result = arg switch
        {
            long l => EnsureRange(l, sbyte.MinValue, sbyte.MaxValue),
            int i => EnsureRange(i, sbyte.MinValue, sbyte.MaxValue),
            string s => (long)sbyte.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => (long)Convert.ToSByte(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(new Xdm.XsTypedInteger(result, "byte"));
    }

    private static long EnsureRange(long val, long min, long max) =>
        val >= min && val <= max ? val : throw new XQueryRuntimeException("FORG0001", $"Value {val} out of range for xs:byte");
}
