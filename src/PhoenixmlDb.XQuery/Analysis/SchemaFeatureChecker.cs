using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Walks the AST to detect schema-aware features (validate expressions,
/// schema-element/schema-attribute tests) and reports errors when no
/// <see cref="ISchemaProvider"/> is registered.
/// </summary>
internal sealed class SchemaFeatureChecker : XQueryExpressionWalker
{
    private readonly List<AnalysisError> _errors = new();

    public IReadOnlyList<AnalysisError> Errors => _errors;

    public override object? VisitValidateExpression(ValidateExpression expr)
    {
        _errors.Add(new AnalysisError(
            "XQST0075",
            "Schema Validation Feature requires PhoenixmlDb.XQuery.Schema",
            expr.Location));

        // Still walk children to catch any nested schema features
        Walk(expr.Expression);
        return null;
    }

    // schema-element() and schema-attribute() appear as NodeTest within StepExpression.
    // The walker visits StepExpression; we override it to inspect the NodeTest.
    public override object? VisitStepExpression(StepExpression expr)
    {
        CheckNodeTest(expr.NodeTest);
        // Walk predicates
        foreach (var pred in expr.Predicates)
            Walk(pred);
        return null;
    }

    // Also check InstanceOfExpression, TreatExpression, etc. that reference KindTests
    // in their TargetType — schema-element/attribute can appear in sequence types.
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

    public override object? VisitSchemaImport(SchemaImportExpression expr)
    {
        _errors.Add(new AnalysisError(
            "XQST0009",
            $"Schema Import Feature requires PhoenixmlDb.XQuery.Schema (import schema \"{expr.TargetNamespace}\")",
            expr.Location));
        return null;
    }

    private void CheckNodeTest(NodeTest? test)
    {
        if (test is SchemaElementTest set)
        {
            _errors.Add(new AnalysisError(
                "XPST0008",
                $"Schema element declaration '{set}' not found in in-scope schema definitions (requires PhoenixmlDb.XQuery.Schema)",
                null));
        }
        else if (test is SchemaAttributeTest sat)
        {
            _errors.Add(new AnalysisError(
                "XPST0008",
                $"Schema attribute declaration '{sat}' not found in in-scope schema definitions (requires PhoenixmlDb.XQuery.Schema)",
                null));
        }
    }

    private void CheckSequenceType(XdmSequenceType? seqType)
    {
        if (seqType is null) return;
        if (seqType.ItemType == ItemType.SchemaElement)
        {
            _errors.Add(new AnalysisError(
                "XPST0008",
                "schema-element() in sequence type requires PhoenixmlDb.XQuery.Schema",
                null));
        }
        else if (seqType.ItemType == ItemType.SchemaAttribute)
        {
            _errors.Add(new AnalysisError(
                "XPST0008",
                "schema-attribute() in sequence type requires PhoenixmlDb.XQuery.Schema",
                null));
        }
    }
}
