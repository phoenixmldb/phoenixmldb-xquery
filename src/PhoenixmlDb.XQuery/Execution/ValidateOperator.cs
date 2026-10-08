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
/// Validate expression operator: serializes the node and delegates to <see cref="ISchemaProvider.ValidateXml"/>.
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
            // Exactly one node: a second one overwrote the first and was validated in its place
            // (QT3 validateexpr-1).
            if (item is XdmNode n && node is null)
                node = n;
            else
                throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQTY0030",
                    "Validate expression requires a single document or element node.");
        }

        if (node is null)
            // XQTY0030 (XQuery 3.1 §3.21): the operand is not exactly one document or element node. This
            // raised XQDY0025, the code for a duplicate attribute name (QT3 XQTY0030, K-CombinedErrorCodes-9..12).
            throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQTY0030",
                "Validate expression requires a single document or element node.");

        XdmElement? root;
        if (node is XdmElement operandElement)
        {
            root = operandElement;
        }
        else if (node is XdmDocument operandDocument)
        {
            // XQDY0061: a document must have exactly one element child and no text children.
            root = null;
            foreach (var childId in operandDocument.Children)
            {
                var child = context.LoadNode(childId);
                if (child is XdmText || (child is XdmElement && root != null))
                    throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQDY0061",
                        "The document node validated must have exactly one element child and no text node children.");
                root = child as XdmElement ?? root;
            }
            if (root is null)
                throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQDY0061",
                    "The document node validated must have exactly one element child.");
        }
        else
        {
            // An attribute, text, comment or processing-instruction node. These reached the
            // validator as markup with no root element and failed with its parse error.
            throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQTY0030",
                "Validate expression requires a single document or element node.");
        }

        // XQDY0084: strict validation needs a top-level declaration for the element itself. The
        // validator reports the same thing, but as one more validity error with no code.
        if (Mode == ValidationMode.Strict && TypeName is null)
        {
            var rootNamespace = root.Namespace == NamespaceId.None
                ? "" : context.NamespaceResolver?.Invoke(root.Namespace) ?? "";
            if (!schemaProvider.HasElementDeclaration(rootNamespace, root.LocalName))
                throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQDY0084",
                    $"No top-level element declaration for '{{{rootNamespace}}}{root.LocalName}': " +
                    "strict validation requires one.");
        }

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
            var annotated = WithinRegexLimit(context, () => schemaProvider.ValidateAndAnnotate(xml, builder, Mode,
                TypeName?.NamespaceUri, TypeName?.LocalName));
            if (annotated != null)
            {
                // Validating an element yields an element (XQuery 3.1 §3.21); the annotating
                // parse builds a document around it, so hand back its document element.
                if (node is XdmElement && annotated is XdmDocument annotatedDoc)
                {
                    foreach (var childId in annotatedDoc.Children)
                    {
                        if (context.NodeProvider.GetNode(childId) is XdmElement validatedElement)
                        {
                            // A new element, not a child of the parse's document: $v/.. is empty.
                            validatedElement.Parent = null;
                            yield return validatedElement;
                            yield break;
                        }
                    }
                }
                yield return annotated;
                yield break;
            }
        }

        WithinRegexLimit<object?>(context, () =>
        {
            schemaProvider.ValidateXml(xml, Mode, TypeName?.NamespaceUri, TypeName?.LocalName);
            return null;
        });
        yield return node;
    }

    /// <summary>
    /// Validation matches the schema's pattern facets; one that runs past the query's regex limit
    /// ends as the same error a timed-out fn:matches gives.
    /// </summary>
    private static T WithinRegexLimit<T>(QueryExecutionContext context, Func<T> validate)
    {
        try
        {
            Functions.XQueryRegexHelper.ThrowIfCancelled(context);
            return validate();
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException ex)
        {
            throw Functions.XQueryRegexHelper.MatchTimedOut(context, ex);
        }
    }
}
