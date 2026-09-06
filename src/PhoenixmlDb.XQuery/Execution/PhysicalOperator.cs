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
/// Base class for physical operators in the execution plan.
/// </summary>
public abstract class PhysicalOperator
{
    /// <summary>
    /// Executes the operator and yields results.
    /// </summary>
    public abstract IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context);

    /// <summary>
    /// Estimated cost for this operator.
    /// </summary>
    public double EstimatedCost { get; init; }

    /// <summary>
    /// Estimated result cardinality.
    /// </summary>
    public long EstimatedCardinality { get; init; }

    /// <summary>
    /// Source location of the AST node this operator was generated from, or <c>null</c>
    /// when not populated by the optimizer. Used to enrich runtime
    /// <see cref="Functions.XQueryException"/>s with module/line/column info so users can
    /// pinpoint errors in multi-module XSLT/XQuery (e.g. Docbook TNG stylesheets).
    /// </summary>
    public SourceLocation? Location { get; init; }

    /// <summary>
    /// Produces a short, human-readable description of an XDM item's runtime type for
    /// inclusion in error messages (e.g. <c>"xs:integer 42"</c>, <c>"xs:string \"foo\""</c>,
    /// <c>"map"</c>, <c>"array"</c>). Used by axis-step operators to enrich XPTY0020.
    /// Truncates long string values to keep messages readable.
    /// </summary>
    protected static string DescribeItemType(object item) => item switch
    {
        null => "empty sequence",
        XdmNode n => $"node ({n.NodeKind})",
        bool b => $"xs:boolean {(b ? "true" : "false")}",
        int i => $"xs:integer {i}",
        long l => $"xs:integer {l}",
        decimal d => $"xs:decimal {d}",
        double dbl => $"xs:double {dbl}",
        float f => $"xs:float {f}",
        string s => s.Length > 40
            ? $"xs:string \"{s[..40]}…\""
            : $"xs:string \"{s}\"",
        IDictionary<object, object?> => "map",
        List<object?> => "array",
        _ => $"item of type {item.GetType().Name}"
    };
}
