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
        object? value = null;
        int itemCount = 0;
        await foreach (var item in Operand.ExecuteAsync(context))
        {
            value = item;
            itemCount++;
            if (itemCount > 1)
                break; // More than one item — not castable
        }

        if (itemCount == 0)
        {
            yield return TargetType.Occurrence == Occurrence.ZeroOrOne;
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
            var lexical = QueryExecutionContext.Atomize(value)?.ToString() ?? "";
            yield return provider.TryCastToSchemaSimpleType(
                TargetType.SchemaTypeNamespace, schemaLocalName, lexical);
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
}
