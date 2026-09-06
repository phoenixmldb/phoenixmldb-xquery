using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Lexical parsing shared by the six format-date / format-dateTime / format-time overloads.
/// </summary>
internal static class DateTimeLexicalParse
{
    /// <summary>
    /// Parses a lexical date/time form, raising FORG0001 rather than a raw .NET FormatException.
    /// </summary>
    /// <remarks>
    /// Casting an invalid lexical form is FORG0001 per XPath 3.1 §19.1. DateTimeOffset.Parse
    /// throws FormatException, which surfaced as a .NET message with no error code — the same
    /// "error names the wrong thing" fault as the XPTY0004 it sits beside. It became easy to hit
    /// once untyped node arguments started reaching this arm: format-time(@dateTimeAttr, ...)
    /// SHOULD fail, because a dateTime lexical form is not a valid xs:time, and it should say so
    /// as FORG0001.
    /// </remarks>
    internal static DateTimeOffset ParseDateTimeLexical(string s, string targetType, Ast.ExecutionContext context)
    {
        try
        {
            return DateTimeOffset.Parse(s, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            throw context.Error("FORG0001", $"Cannot cast '{s}' to {targetType}");
        }
    }

    /// <summary>Parses an xs:time lexical form, raising FORG0001 on a bad value.</summary>
    internal static DateTimeOffset ParseTimeLexical(string s, Ast.ExecutionContext context)
    {
        try
        {
            return new DateTimeOffset(DateTime.MinValue.Add(TimeOnly.Parse(s, CultureInfo.InvariantCulture).ToTimeSpan()));
        }
        catch (FormatException)
        {
            throw context.Error("FORG0001", $"Cannot cast '{s}' to xs:time");
        }
    }
}
