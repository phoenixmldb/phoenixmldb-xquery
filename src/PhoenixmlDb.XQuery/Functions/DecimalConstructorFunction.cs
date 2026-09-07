using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:decimal($arg)</summary>
public sealed class DecimalConstructorFunction : TypeConstructorFunction
{
    public DecimalConstructorFunction() : base("decimal") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        try
        {
            var result = arg switch
            {
                decimal d => d,
                long l => (decimal)l,
                int i => (decimal)i,
                double dbl => (decimal)dbl,
                float f => (decimal)f,
                bool bv => bv ? 1m : 0m,
                string s => decimal.Parse(s.Trim(), CultureInfo.InvariantCulture),
                Xdm.XsUntypedAtomic ua => decimal.Parse(ua.Value.Trim(), CultureInfo.InvariantCulture),
                Xdm.XsAnyUri uri => decimal.Parse(uri.Value.Trim(), CultureInfo.InvariantCulture),
                _ => Convert.ToDecimal(arg, CultureInfo.InvariantCulture)
            };
            return ValueTask.FromResult<object?>(result);
        }
        catch (FormatException)
        {
            throw context.Error("FORG0001", $"Cannot cast '{arg}' to xs:decimal");
        }
        catch (OverflowException)
        {
            throw context.Error("FORG0001", $"Cannot cast '{arg}' to xs:decimal: value out of range");
        }
    }
}
