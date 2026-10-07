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
/// Castable expression: expr castable as type → boolean.
/// </summary>
public sealed class CastableOperator : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required XdmSequenceType TargetType { get; init; }
    public bool OperandIsStringLiteral { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // The operand is atomized first, and an atomization error is an error, not "false": a map
        // or function item has no typed value (FOTY0013; QT3 CastableAs666/668). The whole
        // operand was taken as is, and the catch-all below turned that into false. Atomizing
        // flattens an array, so [1] is one item and [1, 2] two.
        object? value = null;
        int itemCount = 0;
        await foreach (var item in Operand.ExecuteAsync(context))
        {
            foreach (var atom in AtomizedItems(item))
            {
                value = atom;
                itemCount++;
            }
            if (itemCount > 1)
                break; // More than one item — not castable
        }

        if (itemCount == 0)
        {
            yield return TargetType.Occurrence == Occurrence.ZeroOrOne || TargetType.AllowsEmpty;
            yield break;
        }

        // castable as allows at most one item; sequences of length > 1 are never castable
        if (itemCount > 1)
        {
            yield return false;
            yield break;
        }

        // A SCHEMA-DEFINED target type: the engine has no value space for it, so hand the
        // lexical form to the schema provider, which owns facet validation. Absent-or-complex
        // type throws (a static error about the query); value-does-not-match is a plain false.
        if (TargetType.SchemaTypeLocalName is { } schemaLocalName)
        {
            var provider = context.SchemaProvider
                ?? throw new XQueryRuntimeException("XPST0051",
                    $"'{{{TargetType.SchemaTypeNamespace}}}{schemaLocalName}' is a schema-defined type, " +
                    "but no schema provider is registered.");
            // A typed value against a union: castable exactly when the cast to some member
            // succeeds. Judged by its text, xs:gYear("2001") was castable to a union of integer
            // and date, to neither of which a gYear can be cast.
            if (QueryExecutionContext.Atomize(value) is { } typedOperand and not (string or Xdm.XsUntypedAtomic or object?[])
                && provider.GetSchemaSimpleType(TargetType.SchemaTypeNamespace, schemaLocalName)
                    is { Variety: SchemaSimpleTypeVariety.Union, IsDerivedByRestriction: false } union)
            {
                bool castableToMember;
                try
                {
                    TypeCastHelper.CastToSchemaUnion(typedOperand, union, provider, context);
                    castableToMember = true;
                }
                catch (Exception ex) when (ex is XQueryRuntimeException or FormatException or OverflowException or InvalidCastException)
                {
                    castableToMember = false;
                }
                yield return castableToMember;
                yield break;
            }

            string? lexical;
            try
            {
                lexical = QueryExecutionContext.Atomize(value) is { } atomic
                    ? TypeCastHelper.SchemaCastLexical(atomic, TargetType.SchemaTypeNamespace, schemaLocalName, provider, context)
                    : "";
            }
            catch (Exception ex) when (ex is XQueryRuntimeException or FormatException or OverflowException or InvalidCastException)
            {
                // Not castable to the target's built-in base, so not castable to the target.
                lexical = null;
            }
            yield return lexical != null && TypeCastHelper.SchemaTypeAccepts(
                provider, TargetType.SchemaTypeNamespace, schemaLocalName, lexical, context);
            yield break;
        }

        // A built-in LIST type (xs:IDREFS/NMTOKENS/ENTITIES): split the lexical form on
        // whitespace and require EVERY token to be castable to the member type. An empty list
        // is not castable — XSD list types have minLength 1, and QT3 asserts false for "".
        if (TargetType.ListMemberLocalName is { } memberType)
        {
            var lexical = QueryExecutionContext.Atomize(value)?.ToString() ?? "";
            var tokens = lexical.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) { yield return false; yield break; }
            var allTokensValid = true;
            foreach (var token in tokens)
            {
                try { TypeCastHelper.NormalizeStringSubtype(token, memberType); }
                catch { allTokensValid = false; break; }
            }
            yield return allTokensValid;
            yield break;
        }

        // XQuery 3.0+ permits castable as xs:QName against computed strings (see QT3
        // CastableExpr and CastExpr test suites, bug 16059). The castable result is
        // determined purely by whether the lexical form is a valid xs:QName at runtime.

        bool castable;
        try
        {
            var castResult = TypeCastHelper.CastValue(value, TargetType.ItemType);
            // Use LocalTypeName (set regardless of prefix) for derived-type checks.
            var localName = TargetType.LocalTypeName ?? TargetType.UnprefixedTypeName;
            if (localName != null && castResult is long l)
                TypeCastHelper.ValidateIntegerSubtype(l, localName);
            else if (localName != null && castResult is BigInteger bi)
                TypeCastHelper.ValidateIntegerSubtype(bi, localName);
            TypeCastHelper.ValidateDateTimeStamp(castResult, localName);
            if (TargetType.ItemType == ItemType.String && localName != null)
            {
                var cs = castResult is Xdm.XsTypedString ts2 ? ts2.Value : castResult as string;
                if (cs != null)
                    TypeCastHelper.NormalizeStringSubtype(cs, localName);
            }
            castable = true;
        }
        catch
        {
            castable = false;
        }
        yield return castable;
    }

    /// <summary>
    /// The operand item as atomization sees it: a map or function item throws FOTY0013, an array
    /// yields its atomized members, and anything else passes through for the cast to atomize.
    /// </summary>
    private static IEnumerable<object?> AtomizedItems(object? item)
    {
        if (item is not (IDictionary<object, object?> or XQueryFunction or List<object?>))
            return [item];
        return QueryExecutionContext.Atomize(item) switch
        {
            null => [],
            object?[] many => many,
            var one => [one],
        };
    }
}
