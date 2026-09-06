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
/// Validate expression operator: delegates to <see cref="ISchemaProvider.Validate"/>.
/// QueryEngine defaults to an XsdSchemaProvider; when a caller explicitly opts out by
/// passing <c>null</c>, validation can't run — we surface that as a runtime error.
/// </summary>
public sealed class ValidateOperator : PhysicalOperator
{
    public required PhysicalOperator ExpressionOperator { get; init; }
    public required ValidationMode Mode { get; init; }
    public Ast.XdmTypeName? TypeName { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var schemaProvider = context.SchemaProvider
            ?? throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQDY0027",
                "Validate expression requires a registered ISchemaProvider — " +
                "QueryEngine was constructed with schemaProvider: null. " +
                "Use the default XsdSchemaProvider or supply a custom implementation.");

        // Materialize the expression result
        XdmNode? node = null;
        await foreach (var item in ExpressionOperator.ExecuteAsync(context))
        {
            if (item is XdmNode n)
                node = n;
            else
                throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQDY0025",
                    "Validate expression requires a single document or element node.");
        }

        if (node is null)
            throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQDY0025",
                "Validate expression requires a single document or element node.");

        // Serialize the XDM tree to XML markup before validation.
        // Calling schemaProvider.Validate(node, ...) would route through XdmNode.StringValue,
        // which collapses the tree to its concatenated text content (e.g. an element with
        // children becomes the joined text of those children) — the validator then sees
        // text where structure should be and rejects perfectly valid documents. We have
        // the node provider in context here; let the operator serialize properly and hand
        // the schema provider an XML string it can parse with the right structure.
        var xml = PhoenixmlDb.XQuery.Functions.SerializeFunction.SerializeNodeToXml(node, context.NodeProvider);

        // If the provider can return an annotating parse and the context exposes a
        // node-builder, take that path — the resulting tree carries TypeAnnotation
        // values from SchemaInfo.SchemaType on every element/attribute that matched
        // a schema declaration. Otherwise fall through to the validation-only path
        // and return the original node unchanged (legacy behaviour).
        if (context.NodeProvider is INodeBuilder builder)
        {
            var annotated = schemaProvider.ValidateAndAnnotate(xml, builder, Mode,
                TypeName?.NamespaceUri, TypeName?.LocalName);
            if (annotated != null)
            {
                yield return annotated;
                yield break;
            }
        }

        schemaProvider.ValidateXml(xml, Mode, TypeName?.NamespaceUri, TypeName?.LocalName);
        yield return node;
    }
}
