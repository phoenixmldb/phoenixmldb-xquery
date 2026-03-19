using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Function call expression (fn:name(args)).
/// </summary>
public sealed class FunctionCallExpression : XQueryExpression
{
    /// <summary>
    /// Function name (QName).
    /// </summary>
    public required QName Name { get; set; }

    /// <summary>
    /// Arguments.
    /// </summary>
    public required IReadOnlyList<XQueryExpression> Arguments { get; init; }

    /// <summary>
    /// Resolved function (set during static analysis).
    /// </summary>
    public XQueryFunction? ResolvedFunction { get; internal set; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitFunctionCallExpression(this);

    public override string ToString()
    {
        var name = Name.Prefix != null ? $"{Name.Prefix}:{Name.LocalName}" : Name.LocalName;
        var args = string.Join(", ", Arguments);
        return $"{name}({args})";
    }
}

/// <summary>
/// Variable reference ($varname).
/// </summary>
public sealed class VariableReference : XQueryExpression
{
    public required QName Name { get; set; }

    /// <summary>
    /// Resolved variable binding (set during static analysis).
    /// </summary>
    public VariableBinding? ResolvedBinding { get; internal set; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitVariableReference(this);

    public override string ToString()
    {
        var name = Name.Prefix != null ? $"{Name.Prefix}:{Name.LocalName}" : Name.LocalName;
        return $"${name}";
    }
}

/// <summary>
/// Variable binding information.
/// </summary>
public sealed class VariableBinding
{
    public required QName Name { get; init; }
    public required XdmSequenceType Type { get; init; }
    public required VariableScope Scope { get; init; }
}

/// <summary>
/// Scope of a variable.
/// </summary>
public enum VariableScope
{
    /// <summary>
    /// Declared in prolog.
    /// </summary>
    Global,

    /// <summary>
    /// Bound in for clause.
    /// </summary>
    For,

    /// <summary>
    /// Bound in let clause.
    /// </summary>
    Let,

    /// <summary>
    /// Positional variable in for clause.
    /// </summary>
    Positional,

    /// <summary>
    /// Function parameter.
    /// </summary>
    Parameter,

    /// <summary>
    /// Bound in quantified expression.
    /// </summary>
    Quantified,

    /// <summary>
    /// Bound in typeswitch/switch.
    /// </summary>
    TypeswitchVariable
}

/// <summary>
/// Named function reference (fn:name#arity).
/// </summary>
public sealed class NamedFunctionRef : XQueryExpression
{
    public required QName Name { get; init; }
    public required int Arity { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitNamedFunctionRef(this);

    public override string ToString()
    {
        var name = Name.Prefix != null ? $"{Name.Prefix}:{Name.LocalName}" : Name.LocalName;
        return $"{name}#{Arity}";
    }
}

/// <summary>
/// Inline function expression (function($x) { $x + 1 }).
/// </summary>
public sealed class InlineFunctionExpression : XQueryExpression
{
    public required IReadOnlyList<FunctionParameter> Parameters { get; init; }
    public XdmSequenceType? ReturnType { get; init; }
    public required XQueryExpression Body { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitInlineFunctionExpression(this);

    public override string ToString()
    {
        var @params = string.Join(", ", Parameters.Select(p =>
            $"${p.Name.LocalName}" + (p.Type != null ? $" as {p.Type}" : "")));
        var ret = ReturnType != null ? $" as {ReturnType}" : "";
        return $"function({@params}){ret} {{ {Body} }}";
    }
}

/// <summary>
/// Function parameter definition.
/// </summary>
public sealed class FunctionParameter
{
    public required QName Name { get; init; }
    public XdmSequenceType? Type { get; init; }
}

/// <summary>
/// Dynamic function call (expr(args)).
/// </summary>
public sealed class DynamicFunctionCallExpression : XQueryExpression
{
    public required XQueryExpression FunctionExpression { get; init; }
    public required IReadOnlyList<XQueryExpression> Arguments { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitDynamicFunctionCallExpression(this);

    public override string ToString()
    {
        var args = string.Join(", ", Arguments);
        return $"{FunctionExpression}({args})";
    }
}

/// <summary>
/// Placeholder for partial function application (fn(?, 1)).
/// </summary>
public sealed class ArgumentPlaceholder : XQueryExpression
{
    public static ArgumentPlaceholder Instance { get; } = new();

    private ArgumentPlaceholder() { }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitArgumentPlaceholder(this);

    public override string ToString() => "?";
}

/// <summary>
/// Base class for XQuery functions.
/// </summary>
public abstract class XQueryFunction
{
    public abstract QName Name { get; }
    public abstract XdmSequenceType ReturnType { get; }
    public abstract IReadOnlyList<FunctionParameterDef> Parameters { get; }

    public abstract ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        ExecutionContext context);

    public int Arity => Parameters.Count;

    /// <summary>
    /// Whether this function accepts a variable number of arguments (arity is the minimum).
    /// </summary>
    public virtual bool IsVariadic => false;

    /// <summary>
    /// Maximum arity for variadic functions. Defaults to int.MaxValue (unbounded).
    /// Override to set an upper bound (e.g., function-available accepts 1-2 args).
    /// </summary>
    public virtual int MaxArity => IsVariadic ? int.MaxValue : Arity;

    /// <summary>
    /// Error code to raise when this function is called dynamically (via function reference).
    /// Null means dynamic calls are allowed. XSLT context-dependent functions like
    /// current-group(), current-grouping-key(), and current() set this to their error codes.
    /// </summary>
    public virtual string? DynamicCallErrorCode => null;

    /// <summary>
    /// Whether this is an anonymous function (inline function expression / closure).
    /// fn:function-name() returns empty sequence for anonymous functions.
    /// </summary>
    public virtual bool IsAnonymous => false;
}

/// <summary>
/// Function parameter definition for function registry.
/// </summary>
public sealed class FunctionParameterDef
{
    public required QName Name { get; init; }
    public required XdmSequenceType Type { get; init; }
}

/// <summary>
/// Placeholder for execution context (defined elsewhere).
/// </summary>
public interface ExecutionContext
{
    /// <summary>
    /// Gets the current context item.
    /// </summary>
    object? ContextItem { get; }

    /// <summary>
    /// Gets the static base URI for this execution context.
    /// Used by fn:static-base-uri() and fn:resolve-uri($relative).
    /// </summary>
    string? StaticBaseUri => null;
}
