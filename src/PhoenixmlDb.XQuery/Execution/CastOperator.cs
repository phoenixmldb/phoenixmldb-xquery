using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Cast expression: expr cast as type.
/// </summary>
public sealed class CastOperator : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required XdmSequenceType TargetType { get; init; }
    public bool OperandIsStringLiteral { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // XQuery 3.1 §19.1: cast expression operand must be a singleton (or empty with ?)
        object? value = null;
        int count = 0;
        await foreach (var item in Operand.ExecuteAsync(context))
        {
            count++;
            if (count == 1)
                value = item;
            else
                throw new XQueryRuntimeException("XPTY0004",
                    "Cast expression operand is not a single atomic value");
        }

        if (count == 0)
        {
            if (TargetType.Occurrence == Occurrence.ZeroOrOne || TargetType.AllowsEmpty)
                yield break;
            throw new XQueryRuntimeException("XPTY0004",
                "Empty sequence cannot be cast to non-optional type");
        }

        // XPath/XQuery 3.1 §19.1.1: the operand is first atomized with fn:data(). For a node,
        // this yields the typed value (xs:untypedAtomic for untyped elements/attrs). Casting to a
        // namespace-sensitive type (xs:QName, xs:NOTATION) from a node IS allowed inside an explicit
        // cast expression in XQuery 3.0+ (see bug 16089), but NOT as an implicit argument coercion
        // (that remains XPTY0117 — see CastAsNamespaceSensitiveType tests).
        value = QueryExecutionContext.AtomizeTyped(value);

        // Unwrap XsTypedInteger so the cast machinery sees a plain CLR long.
        // The wrapper carries a subtype tag for instance-of identity, but cast
        // is value-level — re-tagging happens in the target constructor.
        //
        // Not for xs:numeric: that is a UNION type, and a value already an instance of one of its
        // members is returned unchanged, subtype tag included. TypeCastHelper's Numeric arm keeps
        // an XsTypedInteger for exactly that reason, but the tag was stripped here first, so
        // xs:short(256) cast as xs:numeric came back as a plain xs:integer (QT3 xs-numeric-017,
        // xquery#83).
        if (value is Xdm.XsTypedInteger castTi && TargetType.ItemType != ItemType.Numeric) value = castTi.Value;

        // A SCHEMA-DEFINED target type. The schema provider validates the lexical form against
        // the type's facets; on success the value is kept in its lexical form, since this
        // engine has no distinct value space for a schema type and every built-in operation on
        // it works from the string. A facet failure is FORG0001, the ordinary "cannot cast"
        // outcome — matching the castable path, which returns false for the same input.
        // A built-in LIST type (xs:IDREFS/NMTOKENS/ENTITIES) casts to a SEQUENCE: split the
        // lexical form on whitespace and cast each token to the member type. QT3
        // CastAs-ListType-7: "a b c" cast as xs:IDREFS is ('a','b','c') of type xs:IDREF*.
        // An empty list is not a valid value — XSD list types have minLength 1.
        if (TargetType.ListMemberLocalName is { } memberType)
        {
            var listLexical = QueryExecutionContext.Atomize(value)?.ToString() ?? "";
            foreach (var item in TypeCastHelper.CastToListType(listLexical, TargetType.LocalTypeName ?? "", memberType))
                yield return item;
            yield break;
        }

        if (TargetType.SchemaTypeLocalName is { } schemaLocalName)
        {
            var provider = context.SchemaProvider
                ?? throw new XQueryRuntimeException("XPST0051",
                    $"'{{{TargetType.SchemaTypeNamespace}}}{schemaLocalName}' is a schema-defined type, " +
                    "but no schema provider is registered.");
            if (value is null)
                yield break;
            // A list type (or a union that chose a list member) gives several items; yield them
            // one by one, or count() sees a single nested value.
            var castResult = TypeCastHelper.CastToSchemaSimpleType(value, TargetType.SchemaTypeNamespace, schemaLocalName, provider, context);
            if (castResult is object?[] listItems)
            {
                foreach (var listItem in listItems)
                    yield return listItem;
            }
            else
            {
                yield return castResult;
            }
            yield break;
        }

        // XQuery §19.1: cast-to-xs:QName requires operand of static type xs:string/xs:untypedAtomic/xs:QName
        if (TargetType.ItemType == ItemType.QName
            && value is not (string or Xdm.XsUntypedAtomic or PhoenixmlDb.Core.QName))
            throw new XQueryRuntimeException("XPTY0004",
                "cast as xs:QName requires an xs:string, xs:untypedAtomic, or xs:QName operand");

        // XQuery 3.0+ relaxed the XQuery 1.0 rule that required a string literal operand for
        // cast as xs:QName (see QT3 test CastExpr K2-CastAs-32 and bug 16059). Per the current
        // spec, casting a computed string to xs:QName is permitted — the cast proceeds at runtime
        // so long as the value is a valid lexical QName.

        // Special handling for cast as xs:QName: resolve prefix using in-scope namespace bindings
        // from the execution context (including xmlns: from enclosing direct element constructors).
        if (TargetType.ItemType == ItemType.QName && value is (string or Xdm.XsUntypedAtomic))
        {
            yield return TypeCastHelper.CastStringToQName(value is Xdm.XsUntypedAtomic ua ? ua.Value : (string)value, context);
            yield break;
        }

        yield return TypeCastHelper.CastToBuiltIn(value, TargetType, context);
    }
}
