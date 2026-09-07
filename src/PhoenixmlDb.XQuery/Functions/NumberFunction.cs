using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:number($arg) as xs:double
/// </summary>
public sealed class NumberFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "number");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Atomize first — raises FOTY0013 for function items, maps, arrays.
        // Route through the execution context's node provider so storage-deserialized
        // elements (NULL precomputed StringValue, lazily-resolved children) atomize
        // correctly via descendant text-node walking, mirroring fn:string() (#160).
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0], nodeProvider);
        if (arg is null)
            return ValueTask.FromResult<object?>(double.NaN);

        if (arg is double d) return ValueTask.FromResult<object?>(d);
        if (arg is float f) return ValueTask.FromResult<object?>((double)f);
        if (arg is int i) return ValueTask.FromResult<object?>((double)i);
        if (arg is long l) return ValueTask.FromResult<object?>((double)l);
        if (arg is System.Numerics.BigInteger bi) return ValueTask.FromResult<object?>((double)bi);
        if (arg is decimal dec) return ValueTask.FromResult<object?>((double)dec);
        if (arg is bool b) return ValueTask.FromResult<object?>(b ? 1.0 : 0.0);
        // xs:anyURI, xs:QName, date/time, duration, and other non-numeric types return NaN
        if (arg is Xdm.XsAnyUri or Core.QName
            or Xdm.XsDateTime or Xdm.XsDate or Xdm.XsTime or DateTimeOffset or DateOnly or TimeOnly
            or Xdm.XsDuration or Xdm.YearMonthDuration or TimeSpan
            or Xdm.XsGYear or Xdm.XsGYearMonth or Xdm.XsGMonthDay or Xdm.XsGDay or Xdm.XsGMonth)
            return ValueTask.FromResult<object?>(double.NaN);
        if (arg is Xdm.XdmValue xv && (xv.Type == Xdm.XdmType.Base64Binary || xv.Type == Xdm.XdmType.HexBinary))
            return ValueTask.FromResult<object?>(double.NaN);

        var str = arg.ToString()?.Trim();
        if (str is "INF" or "+INF") return ValueTask.FromResult<object?>(double.PositiveInfinity);
        if (str is "-INF") return ValueTask.FromResult<object?>(double.NegativeInfinity);
        if (double.TryParse(str, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var result))
            return ValueTask.FromResult<object?>(result);

        return ValueTask.FromResult<object?>(double.NaN);
    }
}
