using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;


// ═══════════════════════════════════════════════════════════════════════════
// XQuery Update Facility 3.0 — AST node definitions
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Base class for all XQuery Update expressions.
/// Update expressions return Pending Update Lists (PULs) instead of values.
/// </summary>
/// <summary>
/// Base of the Update Facility expressions. Deliberately does NOT implement Accept: it used to,
/// as <c>=&gt; default!</c>, which every subclass inherited, so any analysis pass that visited an
/// insert/delete/rename/replace got null back and threw at compile time (xquery#15). Leaving
/// Accept abstract makes a new update node that forgets to dispatch a build error instead.
/// </summary>
public abstract class UpdateExpression : XQueryExpression
{
}
