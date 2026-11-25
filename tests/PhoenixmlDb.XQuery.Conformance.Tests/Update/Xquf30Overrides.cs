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
/// <remarks>
/// Every state below is the only state of its case, so no later state reads the documents it
/// changes. The sources: employeesNIST (four employee elements with a name and a salary, no
/// hours), employees (three of them) and works-mod (employees with empnum, pnum and hours).
/// </remarks>
public static class Xquf30Overrides
{
    // XQUF 3.0 §7.6 Comma Expression.
    private const string Comma = "XQUF 3.0 §7.6 Comma Expression. Category Rules: \"If any of the operand expressions of the "
        + "comma expression is an updating expression, then the comma expression is an updating expression. The operand "
        + "expressions of a comma expression can be expressions of any category.\" Additional Semantics: \"The operand "
        + "expressions of the comma expression are evaluated, and the XDM instances and pending update lists returned are "
        + "concatenated in operand order.\"";

    // XQUF 3.0 §7.4 Conditional Expression.
    private const string Conditional = "XQUF 3.0 §7.4 Conditional Expression. Category Rules: \"If any branch of the "
        + "conditional expression is an updating expression, then the conditional expression is an updating expression. "
        + "[…] The branches of a conditional expression can be expressions of any category.\" Additional Semantics: \"The "
        + "result of the conditional expression is the XDM instance and pending update list returned by the selected branch.\"";

    // XQUF 3.0 §7.2 Typeswitch Expression.
    private const string Typeswitch = "XQUF 3.0 §7.2 Typeswitch Expression. Category Rules: \"If any branch of the "
        + "typeswitch expression is an updating expression, then the typeswitch expression is an updating expression. […] "
        + "The branches of a typeswitch expression can be expressions of any category.\" Additional Semantics: \"The "
        + "expression in the return clause of the effective case (or default) is then evaluated, resulting in an XDM "
        + "instance and pending update list, which serves as the result of the typeswitch expression.\"";

    // XQUF 3.0 §7.10 Function Calls, and the function conversion rules of XQuery 3.1 §3.1.5.2.
    private const string Argument = "XQUF 3.0 §7.10 Function Calls. Category Rules: \"The argument expressions of a "
        + "function call or partial function application can be expressions of any category.\" An updating expression "
        + "returns the empty sequence as its XDM instance; XQuery 3.1 §3.1.5.2: an argument that does not match the "
        + "parameter's declared type after the function conversion rules is a type error [err:XPTY0004].";

    // XQUF 3.0 §4.2 Function Declaration: an updating function.
    private const string UpdatingFunction = "XQUF 3.0 §4.2 Function Declaration. Category Rules: \"An updating function "
        + "may have a FunctionBody of any category.\" Additional Semantics: \"XQuery Update Facility 3.0 allows an updating "
        + "function to return both a non-empty pending update list and a non-empty XDM instance. If the result of an "
        + "updating function does not match its declared return type (after applying function conversion rules […]), a "
        + "type error is raised [err:XPTY0004].\"";

    // XQUF 3.0 §5.1-5.4: the operands of insert, delete, replace and rename.
    private const string UpdateOperand = "XQUF 3.0 §5.1-5.4. Category Rules: \"The SourceExpr and TargetExpr can be "
        + "expressions of any category\" (insert; likewise the operands of delete, replace and rename). An updating "
        + "expression returns the empty sequence as its XDM instance.";

    // An update whose target is empty.
    private const string EmptyTarget = UpdateOperand + " §5.1, §5.3.1, §5.3.2, §5.4: the target expression is "
        + "evaluated; \"If the result is an empty sequence, [err:XUDY0027] is raised.\"";

    // XQUF 3.0 §5.6 Copy Modify.
    private const string CopyModify = "XQUF 3.0 §5.6 Copy Modify. Category Rules: \"The operand expressions of any of the "
        + "clauses of a copy modify expression can be expressions of any category.\" Semantics: \"The expression in the "
        + "modify clause is evaluated, resulting in a pending update list (denoted $pul) and an XDM instance. The XDM "
        + "instance is discarded […]\"";

    // The value comparison `$var/hours eq 4x` on an untyped hours element, once nothing stops it statically.
    private const string UntypedEq = " With no static error, the comparison runs: XQuery 3.1 §3.7.1, an xs:untypedAtomic "
        + "operand of a value comparison is cast to xs:string, and xs:string eq xs:integer is a type error [err:XPTY0004].";

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

            // ---- Updating functions (XQUF 1.0: XUST0002 for a body that is not updating, XUST0028
            // for a return type, XUST0001 for mixed branches in the body) ----

            // `declare updating function local:insertFunction($e as xs:integer) { $e1 + $e }`, called
            // with xs:integer($input-context/employees[1]/salary[1]). Source employeesNIST: the
            // employees element has no salary child, so the path and the argument are empty, and $e
            // is xs:integer.
            ["id-function-declaration-05"] = new("XPTY0004", null, UpdatingFunction),
            // An updating function whose body is a copy modify expression (simple): the copy of
            // works-mod's second employee without its first hours.
            ["id-function-declaration-012"] = new(null,
                "<employee name=\"John Doe 2\" gender=\"male\"><empnum>E1</empnum><pnum>P2</pnum><hours>20</hours>Text data from Employee[2]</employee>",
                UpdatingFunction),
            // An updating function `as element()` whose body is a copy modify expression: the copy of
            // employeesNIST's first employee without its salary.
            ["id-function-declaration-014"] = new(null,
                "<employee mgr=\"true\" dept=\"Toys\"><name>Smith</name></employee>", UpdatingFunction),
            // The updating function takes `$e as element()` and is called with
            // $input-context/works[1]/employee[1]; the source is employeesNIST, which has no works
            // element, so the argument is empty.
            ["id-function-declaration-016"] = new("XPTY0004", null,
                UpdatingFunction + " XQuery 3.1 §3.1.5.2: the empty sequence does not match element() [err:XPTY0004]."),
            // `declare updating function local:test($emp) as xs:integer { replace value of node
            // $emp/salary with 200000 }`, called with $employees/employees/*[1] (source employees:
            // Smith). The body's XDM instance is empty, not an xs:integer.
            ["function-declaration-02"] = new("XPTY0004", null, UpdatingFunction),
            // `declare updating function local:test($emp) { 12 }`.
            ["function-declaration-03"] = new(null, "12", UpdatingFunction),

            // ---- An updating expression as an operand of insert, delete, replace, rename ----

            // `insert node (insert node <salary>150000</salary> into $var) into $var`: the inner
            // insert is made; the outer one inserts the empty sequence (§5.1: "copies of zero or
            // more nodes").
            ["id-insert-expr-023"] = new(null, "", UpdateOperand),
            // Source employeesNIST. The target of the insert is an insert expression, whose XDM
            // instance is empty (it does not depend on the source).
            ["id-insert-expr-024"] = new("XUDY0027", null, EmptyTarget),
            ["id-insert-expr-025"] = new("XUDY0027", null, EmptyTarget),
            // `delete node (delete node $var/salary[1])`: §5.2, a delete "deletes zero or more nodes".
            ["id-delete-expr-06"] = new(null, "", UpdateOperand),
            // `replace node $var1/salary[1] with (rename node …)`: §5.3.1, the replacement is "a new
            // sequence of zero or more nodes".
            ["id-replace-expr-011"] = new(null, "", UpdateOperand),
            // Source employeesNIST. The target is a rename expression: empty.
            ["id-replace-expr-012"] = new("XUDY0027", null, EmptyTarget),
            // `replace value of node $var1/salary[1] with (rename node …)`: §5.3.2, the new value
            // is "either a single text node or an empty sequence".
            ["id-replace-expr-017"] = new(null, "", UpdateOperand),
            // Source employeesNIST. The target is a rename expression: empty.
            ["id-replace-expr-018"] = new("XUDY0027", null, EmptyTarget),
            ["id-rename-expr-06"] = new("XUDY0027", null, EmptyTarget),

            // ---- Copy modify with a simple modify clause (XQUF 1.0: XUST0002) ----

            // `copy $newVar := $var modify xs:integer(1) + xs:integer(2) return $newVar`: the copy
            // of employeesNIST's second employee, unchanged.
            ["id-transform-expr-013"] = new(null,
                "<employee dept=\"Toys\"><name>Jones</name><salary>60000</salary></employee>", CopyModify),
            // The modify clause is a copy modify expression (simple): it changes a copy of the copy;
            // the first employee's copy is returned unchanged.
            ["id-transform-expr-018"] = new(null,
                "<employee mgr=\"true\" dept=\"Toys\"><name>Smith</name><salary>100000</salary></employee>", CopyModify),

            // ---- Typeswitch branches of both categories (XQUF 1.0: XUST0001) ----

            // typeswitch(xs:int(1)): the selected branch inserts into $input-context/employees[1]/employee[2]
            // or replaces the value of employee[1]/salary[1]. Source employeesNIST: both exist.
            ["id-typeswitch-expr-06"] = new(null, "", Typeswitch),
            ["id-typeswitch-expr-07"] = new(null, "", Typeswitch),
            ["id-typeswitch-expr-013"] = new(null, "", Typeswitch),
            ["id-typeswitch-expr-014"] = new(null, "", Typeswitch),
            ["id-typeswitch-expr-020"] = new(null, "", Typeswitch),
            ["id-typeswitch-expr-021"] = new(null, "", Typeswitch),
            // The selected branch renames $input-context/employees[1]/salary[1]. Source
            // employeesNIST: the employees element has no salary child, so the target is empty.
            ["id-typeswitch-expr-027"] = new("XUDY0027", null, Typeswitch + " " + EmptyTarget),
            ["id-typeswitch-expr-028"] = new("XUDY0027", null, Typeswitch + " " + EmptyTarget),
            // typeswitch(xs:int(10)): `case xs:double` does not match, `case xs:int` replaces the
            // value of the first hours of works-mod's first employee.
            ["id-typeswitch-expr-038"] = new(null, "", Typeswitch),

            // ---- Conditional branches of both categories (XQUF 1.0: XUST0001) ----

            // The selected branch inserts into, deletes, replaces the value of, or renames a child of
            // $input-context/employees[1]/employee[2] or employee[3], or deletes employee[2]. Source
            // employeesNIST: each target exists.
            ["id-conditional-expr-05"] = new(null, "", Conditional),
            ["id-conditional-expr-06"] = new(null, "", Conditional),
            ["id-conditional-expr-011"] = new(null, "", Conditional),
            ["id-conditional-expr-012"] = new(null, "", Conditional),
            ["id-conditional-expr-017"] = new(null, "", Conditional),
            ["id-conditional-expr-018"] = new(null, "", Conditional),
            ["id-conditional-expr-023"] = new(null, "", Conditional),
            // The else branch renames $var/hours[1], $var being $input-context/employees[1]/employee[3].
            // Source employeesNIST: no employee has an hours child, so the target is empty.
            ["id-conditional-expr-024"] = new("XUDY0027", null, Conditional + " " + EmptyTarget),
            // `if (fn:true()) then delete node $var else copy … return $newVar`.
            ["id-conditional-expr-034"] = new(null, "", Conditional),
            // `if ($var/hours eq 45) then … else replace value of node $var/hours with 43`, $var being
            // $input-context/works[1]/employee[1]. Source works-mod: that hours element is untyped "40".
            ["id-conditional-expr-038"] = new("XPTY0004", null, Conditional + UntypedEq),

            // ---- Comma operands of both categories (XQUF 1.0: XUST0001) ----

            // (an insert, delete, replace or rename, (6 idiv 2) + 1). Source employeesNIST: each
            // target (employee[1] or employee[2], its salary) exists.
            ["id-comma-expr-06"] = new(null, "4", Comma),
            ["id-comma-expr-07"] = new(null, "4", Comma),
            ["id-comma-expr-013"] = new(null, "4", Comma),
            ["id-comma-expr-014"] = new(null, "4", Comma),
            ["id-comma-expr-020"] = new(null, "4", Comma),
            ["id-comma-expr-021"] = new(null, "4", Comma),
            ["id-comma-expr-027"] = new(null, "4", Comma),
            ["id-comma-expr-028"] = new(null, "4", Comma),
            // `if ($var/hours eq 41) then ("Test", 123) else replace value of node $var/hours with 43`,
            // $var being $input-context/works[1]/employee[1]. Source works-mod: that hours is untyped "40".
            ["id-comma-expr-038"] = new("XPTY0004", null, Conditional + UntypedEq),

            // ---- An updating argument of a function call (XQUF 1.0: XUST0001) ----

            // local:f(an insert, delete, replace or rename) where the parameter is
            // `$e as xs:anyAtomicType`. Source employeesNIST; the targets exist. The argument is the
            // update's XDM instance: empty.
            ["id-function-call-01"] = new("XPTY0004", null, Argument),
            ["id-function-call-02"] = new("XPTY0004", null, Argument),
            ["id-function-call-03"] = new("XPTY0004", null, Argument),
            ["id-function-call-04"] = new("XPTY0004", null, Argument),
            ["id-function-call-06"] = new("XPTY0004", null, Argument),
        };
}
