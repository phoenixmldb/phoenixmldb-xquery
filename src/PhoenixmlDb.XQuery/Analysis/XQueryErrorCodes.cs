using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Standard XQuery error codes.
/// </summary>
public static class XQueryErrorCodes
{
    // Static errors
    public const string XPST0003 = "XPST0003"; // Static syntax error
    public const string XPST0008 = "XPST0008"; // Undefined variable
    public const string XPST0017 = "XPST0017"; // Undefined function
    public const string XPST0051 = "XPST0051"; // Unknown atomic type
    public const string XPST0081 = "XPST0081"; // Unbound prefix
    public const string XQST0059 = "XQST0059"; // Module not found
    public const string XQST0070 = "XQST0070"; // Reserved namespace prefix
    public const string XQST0071 = "XQST0071"; // Duplicate namespace prefix
    public const string XQST0085 = "XQST0085"; // Empty namespace URI with non-empty prefix
    public const string XQST0066 = "XQST0066"; // Duplicate default namespace declaration
    public const string XQST0033 = "XQST0033"; // Duplicate namespace prefix declaration
    public const string XQST0034 = "XQST0034"; // Duplicate function declaration
    public const string XQST0045 = "XQST0045"; // Function declared in reserved namespace
    public const string XQST0047 = "XQST0047"; // Duplicate module target namespace import
    public const string XQST0048 = "XQST0048"; // Variable/function not in module namespace
    public const string XQST0049 = "XQST0049"; // Duplicate variable declaration
    public const string XQST0088 = "XQST0088"; // Empty module namespace URI

    // Type errors
    public const string XPTY0004 = "XPTY0004"; // Type mismatch
    public const string XPTY0018 = "XPTY0018"; // Path step returns mixed nodes and atomics
    public const string XPTY0019 = "XPTY0019"; // Context item is not a node
    public const string XPTY0020 = "XPTY0020"; // Context item is undefined

    // Dynamic errors
    public const string XQDY0025 = "XQDY0025"; // Duplicate attribute
    public const string XQDY0041 = "XQDY0041"; // Cast error
    public const string XQDY0044 = "XQDY0044"; // Invalid attribute name
    public const string XQDY0064 = "XQDY0064"; // Invalid PI target
    public const string XQDY0072 = "XQDY0072"; // Comment contains --
    public const string XQDY0074 = "XQDY0074"; // Invalid element name
    public const string XQDY0084 = "XQDY0084"; // Element content error
    public const string XQDY0091 = "XQDY0091"; // Invalid namespace prefix
    public const string XQDY0096 = "XQDY0096"; // Invalid namespace URI

    // Serialization errors
    public const string SEPM0004 = "SEPM0004"; // Unsupported parameter
    public const string SEPM0009 = "SEPM0009"; // Omit-xml-declaration incompatible
    public const string SEPM0010 = "SEPM0010"; // Invalid output method

    // Function errors
    public const string FOAR0001 = "FOAR0001"; // Division by zero
    public const string FOAR0002 = "FOAR0002"; // Overflow/underflow
    public const string FOCA0002 = "FOCA0002"; // Invalid lexical value
    public const string FOCH0001 = "FOCH0001"; // Code point not valid
    public const string FOCH0002 = "FOCH0002"; // Unsupported collation
    public const string FODC0001 = "FODC0001"; // No context document
    public const string FODC0002 = "FODC0002"; // Error retrieving resource
    public const string FODC0004 = "FODC0004"; // Invalid collection URI
    public const string FODC0005 = "FODC0005"; // Invalid document URI
    public const string FODT0001 = "FODT0001"; // Overflow in date/time
    public const string FODT0002 = "FODT0002"; // Overflow in duration
    public const string FOER0000 = "FOER0000"; // Unidentified error
    public const string FONS0004 = "FONS0004"; // No namespace for prefix
    public const string FORG0001 = "FORG0001"; // Invalid value
    public const string FORG0006 = "FORG0006"; // Invalid argument type
    public const string FORX0001 = "FORX0001"; // Invalid regex flags
    public const string FORX0002 = "FORX0002"; // Invalid regex
    public const string FORX0003 = "FORX0003"; // Regex matches zero-length
    public const string FORX0004 = "FORX0004"; // Invalid replacement string
    public const string FOTY0012 = "FOTY0012"; // Argument is a function
    public const string FOTY0013 = "FOTY0013"; // Atomization of function
    public const string FOTY0014 = "FOTY0014"; // Numeric operation on duration
    public const string FOTY0015 = "FOTY0015"; // Deep-equal on function
}
