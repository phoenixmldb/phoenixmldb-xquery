namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Source location for error reporting and debugging.
/// </summary>
public sealed record SourceLocation(int Line, int Column, int StartIndex, int EndIndex);

/// <summary>
/// Base type for all XQuery expressions.
/// </summary>
public abstract class XQueryExpression
{
    /// <summary>
    /// Source location for error reporting.
    /// </summary>
    public SourceLocation? Location { get; init; }

    /// <summary>
    /// Static type after type checking (null before analysis).
    /// </summary>
    public XdmSequenceType? StaticType { get; internal set; }

    /// <summary>
    /// Accept a visitor.
    /// </summary>
    public abstract T Accept<T>(IXQueryExpressionVisitor<T> visitor);
}

/// <summary>
/// Represents an XDM sequence type for static typing.
/// </summary>
public sealed class XdmSequenceType
{
    public required ItemType ItemType { get; init; }
    public required Occurrence Occurrence { get; init; }

    /// <summary>
    /// For parameterized map types like map(xs:string, xs:boolean), the key type.
    /// Null for map(*) or non-map types.
    /// </summary>
    public ItemType? MapKeyType { get; init; }

    /// <summary>
    /// For parameterized map types like map(xs:string, xs:boolean), the value type.
    /// Null for map(*) or non-map types.
    /// </summary>
    public ItemType? MapValueType { get; init; }

    /// <summary>
    /// For element(*, type) or attribute(*, type) — the required type annotation.
    /// Null when no type constraint is specified.
    /// </summary>
    public PhoenixmlDb.Xdm.XdmTypeName? TypeAnnotation { get; init; }

    /// <summary>
    /// For element(name) — the required element local name.
    /// Null for element() or element(*) or non-element types.
    /// </summary>
    public string? ElementName { get; init; }

    /// <summary>
    /// For document-node(element(name)) — the required document element local name.
    /// Null for document-node() or non-document types.
    /// </summary>
    public string? DocumentElementName { get; init; }

    /// <summary>
    /// For attribute(name) — the required attribute local name.
    /// Null for attribute() or attribute(*) or non-attribute types.
    /// </summary>
    public string? AttributeName { get; init; }

    /// <summary>
    /// When non-null, the atomic type was resolved from an unprefixed name (no xs: prefix
    /// and no EQName syntax). The value is the original local name (e.g. "string", "integer").
    /// Used by XSLT to validate namespace qualification via xpath-default-namespace.
    /// </summary>
    public string? UnprefixedTypeName { get; init; }

    /// <summary>
    /// For typed function types like function(xs:string, xs:integer) as xs:boolean,
    /// the parameter types. Null for function(*) or non-function types.
    /// </summary>
    public IReadOnlyList<XdmSequenceType>? FunctionParameterTypes { get; init; }

    /// <summary>
    /// For typed function types like function(xs:string) as xs:boolean,
    /// the return type. Null for function(*) or non-function types.
    /// </summary>
    public XdmSequenceType? FunctionReturnType { get; init; }

    /// <summary>
    /// For record types (XPath 4.0): the field definitions.
    /// Each entry maps field name → field type. Null for non-record types.
    /// </summary>
    public IReadOnlyDictionary<string, RecordFieldDef>? RecordFields { get; init; }

    /// <summary>
    /// For record types (XPath 4.0): whether the record is extensible (has trailing *).
    /// </summary>
    public bool RecordExtensible { get; init; }

    /// <summary>
    /// For enum types (XPath 4.0): the allowed string values.
    /// Null for non-enum types.
    /// </summary>
    public IReadOnlyList<string>? EnumValues { get; init; }

    /// <summary>
    /// For union types (XPath 4.0): the member types.
    /// An item matches if it matches any member type.
    /// </summary>
    public IReadOnlyList<XdmSequenceType>? UnionTypes { get; init; }

    public static XdmSequenceType Empty { get; } = new()
    {
        ItemType = ItemType.Empty,
        Occurrence = Occurrence.Zero
    };

    public static XdmSequenceType Item { get; } = new()
    {
        ItemType = ItemType.Item,
        Occurrence = Occurrence.ExactlyOne
    };

    public static XdmSequenceType OptionalItem { get; } = new()
    {
        ItemType = ItemType.Item,
        Occurrence = Occurrence.ZeroOrOne
    };

    public static XdmSequenceType ZeroOrMoreItems { get; } = new()
    {
        ItemType = ItemType.Item,
        Occurrence = Occurrence.ZeroOrMore
    };

    public static XdmSequenceType OneOrMoreItems { get; } = new()
    {
        ItemType = ItemType.Item,
        Occurrence = Occurrence.OneOrMore
    };

    public static XdmSequenceType Node { get; } = new()
    {
        ItemType = ItemType.Node,
        Occurrence = Occurrence.ExactlyOne
    };

    public static XdmSequenceType OptionalNode { get; } = new()
    {
        ItemType = ItemType.Node,
        Occurrence = Occurrence.ZeroOrOne
    };

    public static XdmSequenceType ZeroOrMoreNodes { get; } = new()
    {
        ItemType = ItemType.Node,
        Occurrence = Occurrence.ZeroOrMore
    };

    public static XdmSequenceType Integer { get; } = new()
    {
        ItemType = ItemType.Integer,
        Occurrence = Occurrence.ExactlyOne
    };

    public static XdmSequenceType String { get; } = new()
    {
        ItemType = ItemType.String,
        Occurrence = Occurrence.ExactlyOne
    };

    public static XdmSequenceType OptionalString { get; } = new()
    {
        ItemType = ItemType.String,
        Occurrence = Occurrence.ZeroOrOne
    };

    public static XdmSequenceType OptionalAnyUri { get; } = new()
    {
        ItemType = ItemType.AnyUri,
        Occurrence = Occurrence.ZeroOrOne
    };

    public static XdmSequenceType Boolean { get; } = new()
    {
        ItemType = ItemType.Boolean,
        Occurrence = Occurrence.ExactlyOne
    };

    public static XdmSequenceType Double { get; } = new()
    {
        ItemType = ItemType.Double,
        Occurrence = Occurrence.ExactlyOne
    };

    public static XdmSequenceType Decimal { get; } = new()
    {
        ItemType = ItemType.Decimal,
        Occurrence = Occurrence.ExactlyOne
    };

    public override string ToString()
    {
        var typeStr = ItemType.ToString();
        return Occurrence switch
        {
            Occurrence.Zero => "empty-sequence()",
            Occurrence.ExactlyOne => typeStr,
            Occurrence.ZeroOrOne => $"{typeStr}?",
            Occurrence.ZeroOrMore => $"{typeStr}*",
            Occurrence.OneOrMore => $"{typeStr}+",
            _ => typeStr
        };
    }
}

/// <summary>
/// Item type for sequence types.
/// </summary>
/// <summary>
/// Field definition for record types (XPath 4.0).
/// </summary>
public sealed class RecordFieldDef
{
    /// <summary>Field name.</summary>
    public required string Name { get; init; }
    /// <summary>Field type. Null means any type.</summary>
    public XdmSequenceType? Type { get; init; }
    /// <summary>Whether the field is optional (has ? suffix).</summary>
    public bool Optional { get; init; }
}

public enum ItemType
{
    Empty,
    Item,
    Node,
    Element,
    Attribute,
    Text,
    Comment,
    ProcessingInstruction,
    Document,
    AnyAtomicType,
    String,
    Boolean,
    Integer,
    Decimal,
    Double,
    Float,
    Date,
    DateTime,
    Time,
    Duration,
    YearMonthDuration,
    DayTimeDuration,
    QName,
    AnyUri,
    UntypedAtomic,
    GYearMonth,
    GYear,
    GMonthDay,
    GDay,
    GMonth,
    HexBinary,
    Base64Binary,
    Map,
    Array,
    Function,
    Record,  // XPath 4.0: record(field as type, ...)
    Enum,    // XPath 4.0: enum("value1", "value2", ...)
    Union    // XPath 4.0: union(type1, type2, ...)
}

/// <summary>
/// Occurrence indicator for sequence types.
/// </summary>
public enum Occurrence
{
    Zero,           // empty-sequence()
    ExactlyOne,     // no indicator
    ZeroOrOne,      // ?
    ZeroOrMore,     // *
    OneOrMore       // +
}
