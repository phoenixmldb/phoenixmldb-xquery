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
/// Dynamic function call: $f(args)
/// </summary>
public sealed class DynamicFunctionCallOperator : PhysicalOperator
{
    public required PhysicalOperator FunctionExpression { get; init; }
    public required IReadOnlyList<PhysicalOperator> Arguments { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? funcVal = null;
        int funcItemCount = 0;
        await foreach (var item in FunctionExpression.ExecuteAsync(context))
        {
            funcItemCount++;
            if (funcItemCount == 1) funcVal = item;
            else
                throw new XQueryRuntimeException("XPTY0004",
                    "Dynamic function call requires a single function item, got a sequence");
        }
        if (funcItemCount == 0)
            throw new XQueryRuntimeException("XPTY0004",
                "Dynamic function call requires a single function item, got empty sequence");

        // XPath 3.0: Maps are callable as functions: $map($key) ≡ map:get($map, $key)
        if (funcVal is IDictionary<object, object?> map)
        {
            if (Arguments.Count != 1)
                throw new XQueryRuntimeException("XPTY0004", $"A map used as a function requires exactly 1 argument, but {Arguments.Count} were supplied");
            object? key = null;
            int keyCount = 0;
            if (Arguments.Count > 0)
            {
                await foreach (var item in Arguments[0].ExecuteAsync(context))
                {
                    keyCount++;
                    if (keyCount == 1) key = QueryExecutionContext.AtomizeTyped(item);
                    else throw new XQueryRuntimeException("XPTY0004",
                        "A map used as a function requires a single key, got a sequence");
                }
            }
            if (keyCount == 0)
                throw new XQueryRuntimeException("XPTY0004",
                    "A map used as a function requires a single key, got empty sequence");
            if (key != null && Functions.MapKeyHelper.TryGetValue(map, key, out var mapVal))
            {
                // Map values can be sequences (stored as object?[])
                if (mapVal is object?[] mapArr)
                {
                    foreach (var mv in mapArr)
                        yield return mv;
                }
                else
                    yield return mapVal;
            }
            yield break;
        }

        // XPath 3.0: Arrays are callable as functions: $array($pos) ≡ array:get($array, $pos)
        if (funcVal is IList<object?> array)
        {
            if (Arguments.Count != 1)
                throw new XQueryRuntimeException("XPTY0004", $"An array used as a function requires exactly 1 argument, but {Arguments.Count} were supplied");
            object? key = null;
            if (Arguments.Count > 0)
            {
                await foreach (var item in Arguments[0].ExecuteAsync(context))
                {
                    key = context.AtomizeWithNodes(item);
                    break;
                }
            }
            if (key != null)
            {
                // Arrays require xs:integer keys — decimal/double/float is XPTY0004
                if (key is decimal || key is double || key is float)
                    throw new XQueryRuntimeException("XPTY0004",
                        $"Array function call requires an xs:integer argument, got {key.GetType().Name} value {key}");
                var position = Convert.ToInt32(key);
                if (position < 1 || position > array.Count)
                    throw new XQueryRuntimeException("FOAY0001",
                        $"Array index {position} out of bounds (array size: {array.Count})");
                var member = array[position - 1];
                // Array members that are sequences (object?[]) need unwrapping
                if (member is object?[] memberSeq)
                {
                    foreach (var mv in memberSeq)
                        yield return mv;
                }
                else
                    yield return member;
            }
            yield break;
        }

        if (funcVal is not XQueryFunction func)
            throw new XQueryRuntimeException("XPTY0004", "Value is not a function");

        // XSLT 3.0: Some functions (current-group, current-grouping-key, current) cannot be
        // called dynamically through function references — raise the appropriate error
        if (func.DynamicCallErrorCode is { } errorCode)
            throw new XQueryRuntimeException(errorCode,
                $"Function {func.Name.LocalName}() cannot be called dynamically via a function reference");

        // Check arity: number of arguments must match function's expected arity
        if (!func.IsVariadic && Arguments.Count != func.Arity)
            throw new XQueryRuntimeException("XPTY0004",
                $"Function {func.Name.LocalName}() expects {func.Arity} argument(s), but {Arguments.Count} were supplied");

        // Evaluate arguments
        var args = new List<object?>();
        foreach (var argOp in Arguments)
        {
            var argValues = new List<object?>();
            await foreach (var item in argOp.ExecuteAsync(context))
                argValues.Add(item);
            args.Add(argValues.Count == 1 ? argValues[0] : argValues.ToArray());
        }

        // Validate argument types against declared parameter types (XPath 3.0 §3.1.5.1)
        var funcParams = func.Parameters;
        for (int i = 0; i < args.Count && i < funcParams.Count; i++)
        {
            if (funcParams[i].Type != null)
                TypeCastHelper.ValidateDynamicFunctionArg(args[i], funcParams[i].Type, func.Name.LocalName, i);
        }

        // Per XPath/XQuery spec §3.1.5.1: when the callee is a plain built-in that isn't a
        // ContextBoundFunctionRef (which already carries its captured focus), reset the focus
        // so that e.g. `let $f := name#0 return <a/>/$f()` raises XPDY0002 — the function
        // does not inherit the caller's focus. Inline functions handle this inside InvokeAsync.
        object? result;
        var resetFocus = func is not ContextBoundFunctionRef and not InlineFunctionItem;
        if (resetFocus)
            context.PushContextItem(QueryExecutionContext.AbsentFocus);
        try
        {
            result = await func.InvokeAsync(args, context);
        }
        finally
        {
            if (resetFocus)
                context.PopContextItem();
        }

        // XDM arrays and maps are items, not sequences — yield as-is based on return type
        if (result is IDictionary<object, object?>
            || (result is List<object?> && func.ReturnType?.ItemType is Ast.ItemType.Array or Ast.ItemType.Map))
        {
            yield return result;
        }
        else if (result is IEnumerable<object?> seq && result is not string)
        {
            foreach (var item in seq)
                yield return item;
        }
        else if (result != null)
        {
            yield return result;
        }
    }
}
