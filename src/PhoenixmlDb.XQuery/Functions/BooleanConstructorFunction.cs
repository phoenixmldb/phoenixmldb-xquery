using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:boolean($arg)</summary>
public sealed class BooleanConstructorFunction : TypeConstructorFunction
{
    public BooleanConstructorFunction() : base("boolean") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var result = arg switch
        {
            bool b => b,
            string s => ParseXsBoolean(s.Trim()),
            Xdm.XsUntypedAtomic ua => ParseXsBoolean(ua.Value.Trim()),
            Xdm.XsAnyUri uri => ParseXsBoolean(uri.Value.Trim()),
            long l => l != 0,
            int i => i != 0,
            double d => d != 0 && !double.IsNaN(d),
            float f => f != 0 && !float.IsNaN(f),
            decimal d => d != 0,
            _ => Convert.ToBoolean(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(result);
    }

    private static bool ParseXsBoolean(string s) => s switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:boolean: valid values are 'true', 'false', '1', '0'")
    };
}
