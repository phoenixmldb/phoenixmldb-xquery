using System.Collections.Concurrent;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// The function conversion rules (XQuery 3.1 §3.1.5.2) for a call of a built-in, or of a function
/// registered under the name and arity of a built-in (<see cref="FunctionLibrary.BuiltInOf"/>), with
/// the built-in's parameter types: atomization (an array is its members atomized, a map or a
/// function item is FOTY0013), the cardinality, and the item type (XPTY0004). One helper for the
/// static call, the dynamic call and the partial application, so the three cannot differ: a dynamic
/// call checked only the item type, so <c>let $f := string-length#1 return $f(['ab'])</c> was
/// XPTY0004 where the static call gives 2.
/// </summary>
internal sealed class BuiltInArguments
{
    // Built-ins are the functions of FunctionLibrary.Standard, which live as long as the process; one
    // entry for each built-in and arity that is called.
    private static readonly ConcurrentDictionary<(XQueryFunction BuiltIn, int Arity), BuiltInArguments> s_cache = new();

    private BuiltInArguments(IReadOnlyList<FunctionParameterDef> parameters)
    {
        Parameters = parameters;
        ItemTypeChecks = FunctionCallOperator.ItemTypeChecksFor(parameters);
    }

    /// <summary>
    /// The parameter of each argument. For a variadic built-in called with more arguments than it
    /// declares (<c>fn:concat</c>), each further argument has the type of the last parameter.
    /// </summary>
    internal IReadOnlyList<FunctionParameterDef> Parameters { get; }

    /// <summary>The parameter types whose item types are checked, or null for none.</summary>
    internal XdmSequenceType?[]? ItemTypeChecks { get; }

    /// <summary>The conversion of a call of <paramref name="function"/> with <paramref name="arity"/> arguments, or null when it has no built-in's name and arity.</summary>
    internal static BuiltInArguments? For(XQueryFunction? function, int arity)
    {
        if (FunctionLibrary.BuiltInOf(function, arity) is not { } builtIn)
            return null;
        return s_cache.GetOrAdd((builtIn, arity), static key => new BuiltInArguments(ParametersFor(key.BuiltIn, key.Arity)));
    }

    private static IReadOnlyList<FunctionParameterDef> ParametersFor(XQueryFunction builtIn, int arity)
    {
        var declared = builtIn.Parameters;
        if (!builtIn.IsVariadic || declared.Count == 0 || arity <= declared.Count)
            return [.. declared];
        var all = new FunctionParameterDef[arity];
        for (var i = 0; i < arity; i++)
            all[i] = declared[Math.Min(i, declared.Count - 1)];
        return all;
    }

    /// <summary>Converts <paramref name="args"/> in place for a static call of <paramref name="function"/>, or throws the type error.</summary>
    internal void Convert(XQueryFunction function, object?[] args, QueryExecutionContext context)
        => FunctionCallOperator.ConvertArguments(function, Parameters, ItemTypeChecks, args, context);

    /// <summary>
    /// The same conversion for a dynamic call or a partial application. Its errors are
    /// <see cref="XQueryRuntimeException"/>, as a dynamic call's were before it used this
    /// conversion; a static call's cardinality and FOTY0013 errors are the context's
    /// (<see cref="Functions.XQueryException"/>), as they were. A host catches each where it did.
    /// </summary>
    internal void ConvertForDynamicCall(XQueryFunction function, object?[] args, QueryExecutionContext context)
    {
        try
        {
            Convert(function, args, context);
        }
        catch (Functions.XQueryException e)
        {
            throw new XQueryRuntimeException(e.ErrorCode, e.Message, e);
        }
    }
}
