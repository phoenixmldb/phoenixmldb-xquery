using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:date($arg)</summary>
public sealed class DateConstructorFunction : TypeConstructorFunction
{
    public DateConstructorFunction() : base("date") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is XsDate xd) return ValueTask.FromResult<object?>(xd);
        if (arg is DateOnly d) return ValueTask.FromResult<object?>(new XsDate(d, null));
        if (arg is XsDateTime xdt) return ValueTask.FromResult<object?>(new XsDate(DateOnly.FromDateTime(xdt.Value.DateTime), xdt.HasTimezone ? xdt.Value.Offset : null));
        if (arg is DateTimeOffset dto) return ValueTask.FromResult<object?>(new XsDate(DateOnly.FromDateTime(dto.DateTime), dto.Offset));
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        DateTimeConstructorFunction.ValidateDateYearPrefix(s, "xs:date");
        ValidateDateLexical(s);
        try
        {
            return ValueTask.FromResult<object?>(XsDate.Parse(s));
        }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex)
        {
            throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:date: {ex.Message}");
        }
    }
}
