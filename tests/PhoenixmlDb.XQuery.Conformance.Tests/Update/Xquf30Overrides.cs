namespace PhoenixmlDb.Conformance.Tests.Update;

/// <summary>
/// The XQUF 3.0 result of a state whose expected result in XQUTS 1.0.1 is an XQUF 1.0 rule that
/// XQUF 3.0 changed.
/// </summary>
/// <param name="Error">The error code the state must raise, or <c>null</c>: no error.</param>
/// <param name="Result">
/// With no error: the result the state must give, compared as the suite compares a fragment, or
/// <c>null</c> for any result. Ignored when <paramref name="Error"/> is set.
/// </param>
/// <param name="Citation">The XQUF 3.0 rule that gives this result: section and quoted text.</param>
public sealed record Xquf30Expectation(string? Error, string? Result, string Citation);

/// <summary>
/// XQUTS 1.0.1 tests XQUF 1.0. This engine implements XQuery Update Facility 3.0 (W3C Working
/// Group Note, 24 January 2017). Where 3.0 gives another result than a state's expected result, the
/// state is judged by its 3.0 result, listed here with the rule (Lucas, 2026-11-24). The suite's
/// expected results stay as they are: <c>scripts/xquts-conformance.sh</c> reports the pass count
/// under the suite's 1.0 expectations and the pass count with these overrides, and lists every
/// case that has one. Keyed by the state's query name. Each entry is reviewed: it names the rule.
/// </summary>
public static class Xquf30Overrides
{
    public static readonly IReadOnlyDictionary<string, Xquf30Expectation> States =
        new Dictionary<string, Xquf30Expectation>(StringComparer.Ordinal)
        {
            // `copy $newVar1 := delete node $var/salary[1] modify () return $newVar1`. The suite
            // expects XUST0001 (XQUF 1.0: the copy clause must be a simple expression). In 3.0 the
            // copy clause may be updating; its source gives the empty sequence, not a node.
            ["id-transform-expr-012"] = new(
                Error: "XUTY0013",
                Result: null,
                Citation: "XQUF 3.0 §5.6 Copy Modify. Category Rules: \"The operand expressions of any of the clauses "
                    + "of a copy modify expression can be expressions of any category.\" Semantics: \"The result of "
                    + "evaluating the source expression must be a single node [err:XUTY0013].\""),

            // `copy $newVar := $var modify delete node $newVar/employee[1] return delete node
            // $newVar/employee[2]`. The suite expects XUST0001 (XQUF 1.0: the return clause must be
            // a simple expression). In 3.0 the return clause may be updating: its delete goes into
            // the query's pending update list and changes only the copy; the result is empty.
            ["id-transform-expr-014"] = new(
                Error: null,
                Result: "",
                Citation: "XQUF 3.0 §5.6 Copy Modify. Category Rules: \"If any of the copy modify expression's copy or "
                    + "return clauses have operand expressions that are updating expressions, then the copy modify "
                    + "expression is a updating expression. The operand expressions of any of the clauses of a copy "
                    + "modify expression can be expressions of any category.\" Semantics: \"The result of the copy "
                    + "modify expression is the XDM instance returned, as well as a pending update list constructed by "
                    + "merging the pending update lists returned by any of the copy modify expression's copy or return "
                    + "clause operand expressions using upd:mergeUpdates.\""),
        };
}
