using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Walks the AST to validate schema-aware constructs against the registered
/// <see cref="ISchemaProvider"/>. Raises XPST0008 when a <c>schema-element(Name)</c>
/// or <c>schema-attribute(Name)</c> references a declaration that is not in scope.
/// </summary>
/// <remarks>
/// This checker is invoked when a schema provider is registered (which is the
/// default — every <see cref="Execution.QueryEngine"/> ships with a
/// <see cref="XsdSchemaProvider"/> unless the caller explicitly passes <c>null</c>).
/// It does not gate features behind any package boundary; it only enforces the
/// XQuery 3.1 / XPath 3.1 spec rule that schema-element/attribute references
/// must resolve to an in-scope declaration.
/// </remarks>
internal sealed class SchemaFeatureChecker : XQueryExpressionWalker
{
    private readonly ISchemaProvider _provider;
    private readonly List<AnalysisError> _errors = new();

    public SchemaFeatureChecker(ISchemaProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public IReadOnlyList<AnalysisError> Errors => _errors;

    // schema-element() and schema-attribute() appear as NodeTest within StepExpression.
    public override object? VisitStepExpression(StepExpression expr)
    {
        CheckNodeTest(expr.NodeTest);
        foreach (var pred in expr.Predicates)
            Walk(pred);
        return null;
    }

    // schema-element/attribute can also appear in sequence types (instance of, treat as).
    public override object? VisitInstanceOfExpression(InstanceOfExpression expr)
    {
        Walk(expr.Expression);
        CheckSequenceType(expr.TargetType);
        return null;
    }

    public override object? VisitTreatExpression(TreatExpression expr)
    {
        Walk(expr.Expression);
        CheckSequenceType(expr.TargetType);
        return null;
    }

    private void CheckNodeTest(NodeTest? test)
    {
        if (test is SchemaElementTest set)
        {
            if (!_provider.HasElementDeclaration(set.NamespaceUri ?? "", set.LocalName))
            {
                _errors.Add(new AnalysisError(
                    "XPST0008",
                    $"Schema element declaration '{set}' not found in in-scope schema definitions",
                    null));
            }
        }
        else if (test is SchemaAttributeTest sat)
        {
            if (!_provider.HasAttributeDeclaration(sat.NamespaceUri ?? "", sat.LocalName))
            {
                _errors.Add(new AnalysisError(
                    "XPST0008",
                    $"Schema attribute declaration '{sat}' not found in in-scope schema definitions",
                    null));
            }
        }
    }

    private void CheckSequenceType(XdmSequenceType? seqType)
    {
        if (seqType is null) return;
        if (seqType.ItemType == ItemType.SchemaElement && seqType.SchemaElementName != null)
        {
            if (!_provider.HasElementDeclaration(seqType.SchemaElementNamespace ?? "", seqType.SchemaElementName))
            {
                _errors.Add(new AnalysisError(
                    "XPST0008",
                    $"Schema element declaration '{FormatQName(seqType.SchemaElementNamespace, seqType.SchemaElementName)}' not found in in-scope schema definitions",
                    null));
            }
        }
        else if (seqType.ItemType == ItemType.SchemaAttribute && seqType.SchemaAttributeName != null)
        {
            if (!_provider.HasAttributeDeclaration(seqType.SchemaAttributeNamespace ?? "", seqType.SchemaAttributeName))
            {
                _errors.Add(new AnalysisError(
                    "XPST0008",
                    $"Schema attribute declaration '{FormatQName(seqType.SchemaAttributeNamespace, seqType.SchemaAttributeName)}' not found in in-scope schema definitions",
                    null));
            }
        }
    }

    private static string FormatQName(string? ns, string local)
        => string.IsNullOrEmpty(ns) ? local : $"{{{ns}}}{local}";
}

/// <summary>
/// No-op <see cref="ISchemaProvider"/> used when the engine is configured without one.
/// All schema lookups return false / null / no-op; <c>validate</c> at runtime still throws.
/// Used by the static analyzer to emit XPST0008 for any schema-element/attribute reference
/// in an opt-out configuration.
/// </summary>
internal sealed class NullSchemaProvider : ISchemaProvider
{
    public void ImportSchema(string targetNamespace, IReadOnlyList<string>? locationHints = null)
        => throw new SchemaException("XQST0059",
            $"Cannot import schema for '{targetNamespace}': no schema provider is registered. " +
            "Construct QueryEngine with an XsdSchemaProvider (default) or a custom ISchemaProvider.");

    public bool IsSubtypeOf(Xdm.XdmTypeName actualType, Xdm.XdmTypeName requiredType)
        => actualType == requiredType;

    public bool HasElementDeclaration(Xdm.XdmQName name) => false;
    public bool HasAttributeDeclaration(Xdm.XdmQName name) => false;
    public Xdm.XdmTypeName? GetElementType(Xdm.XdmQName name) => null;
    public Xdm.XdmTypeName? GetAttributeType(Xdm.XdmQName name) => null;
    public bool MatchesSchemaElement(Xdm.Nodes.XdmElement element, Xdm.XdmQName declarationName) => false;
    public bool MatchesSchemaAttribute(Xdm.Nodes.XdmAttribute attribute, Xdm.XdmQName declarationName) => false;

    public Xdm.Nodes.XdmNode Validate(Xdm.Nodes.XdmNode node, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null)
        => throw new SchemaValidationException("XQDY0027",
            "Cannot validate: no schema provider is registered. " +
            "Construct QueryEngine with an XsdSchemaProvider (default) or a custom ISchemaProvider.");

    public void ValidateXml(string xmlContent, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null)
        => throw new SchemaValidationException("XQDY0027",
            "Cannot validate XML: no schema provider is registered.");

    public void ValidateXmlFragment(string xmlFragment, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null,
        IReadOnlyDictionary<string, string>? inScopeNamespaces = null)
        => throw new SchemaValidationException("XQDY0027",
            "Cannot validate XML fragment: no schema provider is registered.");
}
