using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:long($arg)</summary>
public sealed class LongConstructorFunction : TypeConstructorFunction
{
    public LongConstructorFunction() : base("long") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Unwrap an existing XsTypedInteger so the value flows through arithmetic/range checks.
        if (arg is Xdm.XsTypedInteger ti) arg = ti.Value;
        var result = arg switch
        {
            long l => l,
            int i => (long)i,
            string s => long.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => Convert.ToInt64(arg, CultureInfo.InvariantCulture)
        };
        // Tag the result with its declared XSD type so `instance of xs:long` can
        // distinguish this from a bare integer (or from a sibling subtype like
        // xs:nonNegativeInteger).
        return ValueTask.FromResult<object?>(new Xdm.XsTypedInteger(result, "long"));
    }
}
