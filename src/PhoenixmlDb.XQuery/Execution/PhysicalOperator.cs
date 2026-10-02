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
    /// Whether this operator AND every operand it evaluates can run through
    /// <see cref="EvaluateSync"/>. Decided from the operator tree alone, before anything runs,
    /// so a synchronous evaluation never has to abandon itself halfway (which would repeat
    /// side effects such as fn:trace on the asynchronous retry).
    /// </summary>
    /// <remarks>
    /// Why: each <see cref="ExecuteAsync"/> allocates an async iterator state machine, and an
    /// XSLT stylesheet evaluates many small expressions per node. Applying a compiled Schematron
    /// validator, those state machines were ~45% of all bytes allocated. Only operators whose
    /// output is bounded take part, so a lazy consumer (exists(//x) stopping at the first match)
    /// is never turned into a full materialisation.
    /// </remarks>
    internal virtual bool SupportsSync => false;

    /// <summary>
    /// Evaluates synchronously: the empty sequence as <c>null</c>, one item as itself, two or more
    /// as <c>object?[]</c>. Called only when <see cref="SupportsSync"/> is true.
    /// </summary>
    internal virtual object? EvaluateSync(QueryExecutionContext context)
        => throw new NotSupportedException($"{GetType().Name} has no synchronous evaluation");

    /// <summary>
    /// Synchronous evaluation is off in browser WebAssembly and WASI, where a function that does
    /// complete asynchronously could not be waited for (xslt#237), and with PHOENIXML_SYNC_EVAL=0.
    /// </summary>
    internal static readonly bool SyncEvaluationEnabled =
        !OperatingSystem.IsBrowser() && !OperatingSystem.IsWasi()
        && Environment.GetEnvironmentVariable("PHOENIXML_SYNC_EVAL") != "0";

    /// <summary>Whether <paramref name="op"/> should be evaluated through <see cref="EvaluateSync"/>.</summary>
    internal static bool CanEvaluateSync(PhysicalOperator op) => SyncEvaluationEnabled && op.SupportsSync;

    /// <summary>The result of a synchronous evaluation as a list of items (for operand processing).</summary>
    internal static void AddSyncItems(object? result, List<object?> into)
    {
        if (result is object?[] seq) into.AddRange(seq);
        else if (result != null) into.Add(result);
    }

    /// <summary>The first item of a synchronous result, or null.</summary>
    internal static object? FirstSyncItem(object? result)
        => result is object?[] seq ? (seq.Length > 0 ? seq[0] : null) : result;

    /// <summary>Shapes a list of items as a synchronous result.</summary>
    internal static object? SyncResultOf(List<object?> items)
        => items.Count switch { 0 => null, 1 => items[0], _ => items.ToArray() };

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
