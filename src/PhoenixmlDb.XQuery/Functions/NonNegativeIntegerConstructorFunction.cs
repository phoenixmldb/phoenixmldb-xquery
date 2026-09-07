using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:nonNegativeInteger($arg)</summary>
public sealed class NonNegativeIntegerConstructorFunction : TypeConstructorFunction
{
    public NonNegativeIntegerConstructorFunction() : base("nonNegativeInteger") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var val = arg switch
        {
            long l => l,
            int i => (long)i,
            string s => long.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => Convert.ToInt64(arg, CultureInfo.InvariantCulture)
        };
        return val >= 0
            ? ValueTask.FromResult<object?>(new Xdm.XsTypedInteger(val, "nonNegativeInteger"))
            : throw new XQueryRuntimeException("FORG0001", $"Value {val} out of range for xs:nonNegativeInteger (must be >= 0)");
    }
}
