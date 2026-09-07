using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:int($arg)</summary>
public sealed class IntConstructorFunction : TypeConstructorFunction
{
    public IntConstructorFunction() : base("int") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var result = arg switch
        {
            int i => (long)i,
            long l => EnsureRange(l, int.MinValue, int.MaxValue),
            string s => (long)int.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => (long)Convert.ToInt32(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(new Xdm.XsTypedInteger(result, "int"));
    }

    // FORG0001, not OverflowException. A constructor rejecting an out-of-range value is the
    // spec's "invalid value for cast/constructor", and the parallel implementation in
    // TypeCastHelper.ValidateIntegerSubtype has always raised FORG0001 for the identical check.
    // The raw CLR exception escaped the engine as "Value was either too large or too small",
    // which is not an XQuery error at all and could not match any expected code.
    private static long EnsureRange(long val, long min, long max) =>
        val >= min && val <= max ? val : throw new XQueryRuntimeException("FORG0001", $"Value {val} out of range for xs:int");
}
