using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:unsignedInt($arg)</summary>
public sealed class UnsignedIntConstructorFunction : TypeConstructorFunction
{
    public UnsignedIntConstructorFunction() : base("unsignedInt") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var result = arg switch
        {
            long l => EnsureRange(l, 0, uint.MaxValue),
            int i when i >= 0 => (long)i,
            int i => throw new XQueryRuntimeException("FORG0001", $"Value {i} out of range for xs:unsignedInt"),
            string s => (long)uint.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => (long)Convert.ToUInt32(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(new Xdm.XsTypedInteger(result, "unsignedInt"));
    }

    private static long EnsureRange(long val, long min, long max) =>
        val >= min && val <= max ? val : throw new XQueryRuntimeException("FORG0001", $"Value {val} out of range for xs:unsignedInt");
}
