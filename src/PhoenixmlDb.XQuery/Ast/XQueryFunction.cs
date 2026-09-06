using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

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
    /// Minimum arity for variadic functions — the fewest arguments a call may supply.
    /// Defaults to <see cref="Arity"/> (the full parameter count), which preserves
    /// resolution for non-variadic functions and for true-variadic functions whose
    /// declared parameters are all required (e.g. fn:concat, min 2). Functions with
    /// optional <em>trailing</em> arguments (e.g. array:slice, fn:slice, fn:highest,
    /// map:build) override this to the count of required leading parameters so calls
    /// resolve across their whole arity range — not just at <see cref="MaxArity"/>.
    /// Occurrence indicators cannot be used to infer this: fn:concat declares its
    /// parameters as <c>item()*</c> yet still requires two arguments.
    /// </summary>
    public virtual int MinArity => Arity;

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
