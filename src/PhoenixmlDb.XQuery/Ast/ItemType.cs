namespace PhoenixmlDb.XQuery.Ast;

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
    Numeric, // xs:numeric — the union of xs:double, xs:float, xs:decimal (and their subtypes incl. xs:integer)
    SchemaElement, // schema-element(Name) — requires ISchemaProvider
    SchemaAttribute, // schema-attribute(Name) — requires ISchemaProvider

    // Appended rather than grouped with the other node kinds so no existing member's
    // numeric value shifts. namespace-node() previously had no member at all and was
    // parsed as ItemType.Node, which matches ANY node - so `$x instance of
    // namespace-node()` answered true for elements, attributes, text and documents.
    Namespace,
}
