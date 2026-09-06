using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:numeric($arg) — the xs:numeric union type. Casting to xs:numeric always
/// produces an xs:double (XPath/XQuery F&amp;O §19, QT3 xs-numeric-007..010).</summary>
public sealed class NumericConstructorFunction : TypeConstructorFunction
{
    public NumericConstructorFunction() : base("numeric") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);

        try
        {
            var result = arg switch
            {
                double d => d,
                float f => (double)f,
                decimal d => d == 0m ? 0.0 : (double)d,
                long l => (double)l,
                int i => (double)i,
                bool bv => bv ? 1.0 : 0.0,
                string s => DoubleConstructorFunction.ParseXsDoubleStrict(s.Trim()),
                Xdm.XsUntypedAtomic ua => DoubleConstructorFunction.ParseXsDoubleStrict(ua.Value.Trim()),
                Xdm.XsAnyUri uri => DoubleConstructorFunction.ParseXsDoubleStrict(uri.Value.Trim()),
                _ => Convert.ToDouble(arg, CultureInfo.InvariantCulture)
            };
            return ValueTask.FromResult<object?>(result);
        }
        catch (FormatException)
        {
            throw context.Error("FORG0001", $"Cannot cast '{arg}' to xs:numeric");
        }
        catch (OverflowException)
        {
            throw context.Error("FORG0001", $"Cannot cast '{arg}' to xs:numeric: value out of range");
        }
    }
}
