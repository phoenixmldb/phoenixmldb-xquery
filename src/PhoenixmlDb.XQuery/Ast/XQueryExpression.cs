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
/// Represents an XQuery sequence type, combining an <see cref="Ast.ItemType"/> with an
/// <see cref="Ast.Occurrence"/> indicator to describe the type and cardinality of an XDM value.
/// </summary>
/// <remarks>
/// <para>
/// Sequence types are the core of XQuery's type system. They appear in function signatures
/// (<c>function($x as xs:string) as xs:boolean</c>), variable declarations
/// (<c>let $x as xs:integer := 42</c>), and <c>instance of</c> / <c>treat as</c> / <c>cast as</c> expressions.
/// </para>
/// <para>
/// A sequence type has two parts:
/// <list type="bullet">
///   <item><description><see cref="ItemType"/> — the type of each item (e.g., <c>xs:string</c>, <c>element()</c>, <c>item()</c>).</description></item>
///   <item><description><see cref="Occurrence"/> — the cardinality indicator: exactly one (no indicator), <c>?</c> (zero or one), <c>*</c> (zero or more), or <c>+</c> (one or more).</description></item>
/// </list>
/// </para>
/// <para>
/// Common pre-built instances are available as static properties (e.g., <see cref="String"/>,
/// <see cref="Boolean"/>, <see cref="ZeroOrMoreNodes"/>).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Checking a static type after compilation:
/// var result = engine.Compile("1 + 2");
/// var staticType = result.AnalyzedExpression?.StaticType;
/// // staticType.ItemType == ItemType.Integer, staticType.Occurrence == Occurrence.ExactlyOne
/// </code>
/// </example>
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
    /// For parameterized map types like map(xs:string, xs:integer+), the full value sequence type.
    /// Allows checking occurrence and nested type constraints. Null for map(*) or non-map types.
    /// </summary>
    public XdmSequenceType? MapValueSequenceType { get; init; }

    /// <summary>
    /// For parameterized array types like array(xs:string), the member sequence type.
    /// Null for array(*) or non-array types.
    /// </summary>
    public XdmSequenceType? ArrayMemberType { get; init; }

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
    /// For element(ns:name) — the required element namespace URI.
    /// Null when no namespace constraint or for non-element types.
    /// </summary>
    public string? ElementNamespace { get; init; }

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
    /// For attribute(ns:name) — the required attribute namespace URI.
    /// Null when no namespace constraint or for non-attribute types.
    /// </summary>
    public string? AttributeNamespace { get; init; }

    /// <summary>
    /// For processing-instruction("name") — the required PI target name.
    /// Null for processing-instruction() or non-PI types.
    /// </summary>
    public string? PIName { get; init; }

    /// <summary>
    /// For schema-element(name) — the schema element declaration local name.
    /// Null for non-schema-element types. Requires <see cref="ISchemaProvider"/>.
    /// </summary>
    public string? SchemaElementName { get; init; }

    /// <summary>
    /// For schema-element(ns:name) — the schema element declaration namespace URI.
    /// Null when no namespace constraint or for non-schema-element types.
    /// </summary>
    public string? SchemaElementNamespace { get; init; }

    /// <summary>
    /// For schema-attribute(name) — the schema attribute declaration local name.
    /// Null for non-schema-attribute types. Requires <see cref="ISchemaProvider"/>.
    /// </summary>
    public string? SchemaAttributeName { get; init; }

    /// <summary>
    /// For schema-attribute(ns:name) — the schema attribute declaration namespace URI.
    /// Null when no namespace constraint or for non-schema-attribute types.
    /// </summary>
    public string? SchemaAttributeNamespace { get; init; }

    /// <summary>
    /// When non-null, the atomic type was resolved from an unprefixed name (no xs: prefix
    /// and no EQName syntax). The value is the original local name (e.g. "string", "integer").
    /// Used by XSLT to validate namespace qualification via xpath-default-namespace.
    /// </summary>
    public string? UnprefixedTypeName { get; init; }

    /// <summary>
    /// For derived integer types (xs:int, xs:short, xs:long, xs:byte, etc.),
    /// the specific subtype name used for range validation in instance-of checks.
    /// </summary>
    public string? DerivedIntegerType { get; init; }

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

    /// <summary>xs:anyAtomicType? — used for numeric function params (fn:floor, fn:ceiling, fn:round, fn:abs)</summary>
    public static XdmSequenceType OptionalAnyAtomicType { get; } = new()
    {
        ItemType = ItemType.AnyAtomicType,
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

/// <summary>
/// Enumerates the item types available in the XQuery type system.
/// </summary>
/// <remarks>
/// These correspond to the item type keywords in XQuery sequence type syntax (e.g., <c>xs:string</c>,
/// <c>element()</c>, <c>item()</c>). Used in combination with <see cref="Occurrence"/> to form
/// a complete <see cref="XdmSequenceType"/>.
/// </remarks>
/// <seealso cref="XdmSequenceType"/>
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
    Union,   // XPath 4.0: union(type1, type2, ...)
    Notation, // xs:NOTATION — valid in instance of / typeswitch, never matches any atomic value
    Error, // xs:error (XSD 1.1) — the empty union type; no value is ever an instance, cast always fails
    SchemaElement, // schema-element(Name) — requires ISchemaProvider
    SchemaAttribute // schema-attribute(Name) — requires ISchemaProvider
}

/// <summary>
/// Occurrence indicator for sequence types, controlling the cardinality (how many items are allowed).
/// </summary>
/// <remarks>
/// Corresponds to the XQuery occurrence indicators: no suffix means exactly one,
/// <c>?</c> means zero or one, <c>*</c> means zero or more, and <c>+</c> means one or more.
/// For example, <c>xs:string?</c> is <see cref="ItemType.String"/> with <see cref="ZeroOrOne"/>.
/// </remarks>
/// <seealso cref="XdmSequenceType"/>
public enum Occurrence
{
    /// <summary>The empty sequence — <c>empty-sequence()</c>.</summary>
    Zero,
    /// <summary>Exactly one item — no occurrence indicator.</summary>
    ExactlyOne,
    /// <summary>Zero or one item — the <c>?</c> indicator.</summary>
    ZeroOrOne,
    /// <summary>Zero or more items — the <c>*</c> indicator.</summary>
    ZeroOrMore,
    /// <summary>One or more items — the <c>+</c> indicator.</summary>
    OneOrMore
}
