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
            if (TargetType.Occurrence == Occurrence.ZeroOrOne)
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
        if (value is Xdm.XsTypedInteger castTi) value = castTi.Value;

        // A SCHEMA-DEFINED target type. The schema provider validates the lexical form against
        // the type's facets; on success the value is kept in its lexical form, since this
        // engine has no distinct value space for a schema type and every built-in operation on
        // it works from the string. A facet failure is FORG0001, the ordinary "cannot cast"
        // outcome — matching the castable path, which returns false for the same input.
        if (TargetType.SchemaTypeLocalName is { } schemaLocalName)
        {
            var provider = context.SchemaProvider
                ?? throw new XQueryRuntimeException("XPST0051",
                    $"'{{{TargetType.SchemaTypeNamespace}}}{schemaLocalName}' is a schema-defined type, " +
                    "but no schema provider is registered.");
            var lexical = value?.ToString() ?? "";
            if (!provider.TryCastToSchemaSimpleType(TargetType.SchemaTypeNamespace, schemaLocalName, lexical))
                throw new XQueryRuntimeException("FORG0001",
                    $"'{lexical}' is not a valid value for schema type " +
                    $"'{{{TargetType.SchemaTypeNamespace}}}{schemaLocalName}'.");
            yield return lexical;
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
            var s = (value is Xdm.XsUntypedAtomic ua ? ua.Value : (string)value).Trim();
            if (s.Length == 0)
                throw new XQueryRuntimeException("FORG0001", "Cannot cast empty string to xs:QName");
            var colonIdx = s.IndexOf(':', StringComparison.Ordinal);
            if (colonIdx > 0)
            {
                var prefix = s[..colonIdx];
                var localName = s[(colonIdx + 1)..];
                if (!TypeCastHelper.IsValidNCNameLex(prefix) || !TypeCastHelper.IsValidNCNameLex(localName))
                    throw new XQueryRuntimeException("FORG0001",
                        $"'{s}' is not a valid lexical xs:QName");
                string? nsUri = null;
                if (context.PrefixNamespaceBindings != null)
                    context.PrefixNamespaceBindings.TryGetValue(prefix, out nsUri);
                // Built-in predeclared namespace prefixes
                if (string.IsNullOrEmpty(nsUri))
                {
                    nsUri = prefix switch
                    {
                        "fn" => "http://www.w3.org/2005/xpath-functions",
                        "xs" => "http://www.w3.org/2001/XMLSchema",
                        "xsi" => "http://www.w3.org/2001/XMLSchema-instance",
                        "math" => "http://www.w3.org/2005/xpath-functions/math",
                        "map" => "http://www.w3.org/2005/xpath-functions/map",
                        "array" => "http://www.w3.org/2005/xpath-functions/array",
                        "err" => "http://www.w3.org/2005/xqt-errors",
                        "local" => "http://www.w3.org/2005/xquery-local-functions",
                        "xml" => "http://www.w3.org/XML/1998/namespace",
                        _ => null
                    };
                }
                if (string.IsNullOrEmpty(nsUri))
                    throw new XQueryRuntimeException("FONS0004",
                        $"No namespace binding for prefix '{prefix}' in cast as xs:QName");
                var nsId = new Core.NamespaceId((uint)Math.Abs(nsUri.GetHashCode()));
                yield return new Core.QName(nsId, localName, prefix) { RuntimeNamespace = nsUri };
            }
            else
            {
                if (!TypeCastHelper.IsValidNCNameLex(s))
                    throw new XQueryRuntimeException("FORG0001",
                        $"'{s}' is not a valid lexical xs:QName");
                yield return new Core.QName(Core.NamespaceId.None, s);
            }
            yield break;
        }

        var result = TypeCastHelper.CastValue(value, TargetType.ItemType);
        // Validate integer subtype ranges (long, int, unsignedLong, etc. — xs:integer has no bound).
        // Use LocalTypeName so xs:int (prefixed) and int (unprefixed via xpath-default-namespace)
        // both validate; UnprefixedTypeName is reserved for the XSLT XPST0051 contract.
        var typeLocalName = TargetType.LocalTypeName ?? TargetType.UnprefixedTypeName;
        if (typeLocalName != null && result is long l)
            TypeCastHelper.ValidateIntegerSubtype(l, typeLocalName);
        else if (typeLocalName != null && result is BigInteger bi)
            TypeCastHelper.ValidateIntegerSubtype(bi, typeLocalName);
        // Tag the result with its derived-integer subtype so its dynamic type is the
        // cast target (xs:short, xs:long, …), not bare xs:integer. This makes
        // `xs:long(120) cast as xs:short instance of xs:short` hold, matching the
        // tagging performed by the xs:short(...) etc. constructor functions. Untagged
        // integers (literals, arithmetic, xs:integer cast) remain bare xs:integer.
        if (TargetType.DerivedIntegerType is { } derivedInt && derivedInt != "integer"
            && result is long dl)
            result = new Xdm.XsTypedInteger(dl, derivedInt);
        // Normalize/validate xs:string derived subtypes
        if (TargetType.ItemType == ItemType.String && typeLocalName != null)
        {
            var strVal = result is Xdm.XsTypedString ts ? ts.Value : result as string;
            if (strVal != null)
                result = TypeCastHelper.NormalizeStringSubtype(strVal, typeLocalName);
        }
        yield return result;
    }
}
