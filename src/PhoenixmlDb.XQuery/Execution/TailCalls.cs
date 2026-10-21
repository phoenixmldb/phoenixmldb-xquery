using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// A call to a declared function in tail position of a declared function's body, returned to the
/// function invocation instead of made (xquery#296). <see cref="DeclaredFunction"/> runs it next,
/// in a loop: tail recursion, direct or mutual, then uses neither the function-call depth
/// (FOER0000 at 1,000) nor the .NET stack. QT3 numberformat121/122 recurse 5,000 deep.
/// </summary>
internal sealed class TailCallRequest(XQueryFunction function, object?[] arguments)
{
    public XQueryFunction Function { get; } = function;
    public object?[] Arguments { get; } = arguments;
}

/// <summary>
/// A function whose invocation runs tail calls in a loop: <see cref="DeclaredFunction"/>, and a
/// host's own (the XSLT engine's stylesheet functions). A marked call makes a
/// <see cref="TailCallRequest"/> for it when its key equals the caller's: the same kind of
/// function and the same declared return type.
/// </summary>
internal interface ITailCallTarget
{
    string TailCallKey { get; }
}

internal static class TailCalls
{
    /// <summary>
    /// Marks the function calls of <paramref name="op"/>, the plan of a declared function's body,
    /// that are in tail position: whose result is the body's result, nothing applied to it after.
    /// The body itself; the branches of an if, a typeswitch or a switch; the return of a FLWOR of
    /// let clauses only. <paramref name="returnTypeKey"/> is the declared function's return type,
    /// which a callee must have too: the loop applies only the first caller's coercion.
    /// </summary>
    internal static void Mark(PhysicalOperator op, string returnTypeKey)
    {
        switch (op)
        {
            case FunctionCallOperator call:
                call.TailCallerReturnType = returnTypeKey;
                break;
            case IfOperator conditional:
                Mark(conditional.Then, returnTypeKey);
                Mark(conditional.Else, returnTypeKey);
                break;
            case FlworOperator flwor when flwor.OtherwiseOperator is null
                && flwor.Clauses.All(c => c is LetClauseOperator):
                Mark(flwor.ReturnOperator, returnTypeKey);
                break;
            case TypeswitchOperator typeswitch:
                foreach (var c in typeswitch.Cases)
                    Mark(c.Result, returnTypeKey);
                Mark(typeswitch.DefaultResult, returnTypeKey);
                break;
            case SwitchOperator @switch:
                foreach (var c in @switch.Cases)
                    Mark(c.Result, returnTypeKey);
                Mark(@switch.Default, returnTypeKey);
                break;
        }
    }

    /// <summary>
    /// The key a caller and a callee must share: the kind of function (<paramref name="kind"/>)
    /// and the declared return type, or none.
    /// </summary>
    internal static string ReturnTypeKey(string kind, XdmSequenceType? type) => kind + ":" + (type?.ToString() ?? "");
}
