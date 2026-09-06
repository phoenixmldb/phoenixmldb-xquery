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
/// Named function reference: fn:name#arity
/// </summary>
public sealed class NamedFunctionRefOperator : PhysicalOperator
{
    public required QName Name { get; init; }
    public required int Arity { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        var func = context.Functions.Resolve(Name, Arity);
        if (func == null)
            throw new XQueryRuntimeException("XPST0017", $"Function {Name.LocalName}#{Arity} not found");

        // Per XPath 3.1 §3.1.6, a named function reference to a context-dependent function
        // captures the focus of its host expression at creation time. Wrap these so the
        // captured focus is restored on each invocation. This applies both to arity-0
        // functions (e.g., fn:name#0) and arity-1 functions that implicitly use the
        // context node (e.g., fn:lang#1, fn:id#1, fn:element-with-id#1).
        if (IsContextCaptureFunction(Name))
        {
            object? capturedItem;
            int capturedPos = 1, capturedSize = 1;
            try
            {
                capturedItem = context.ContextItem;
                capturedPos = context.Position;
                capturedSize = context.Last;
            }
            catch (XQueryRuntimeException) { capturedItem = QueryExecutionContext.AbsentFocus; }
            yield return new ContextBoundFunctionRef(func, capturedItem, context.StaticBaseUri, capturedPos, capturedSize);
            yield break;
        }

        // fn:function-lookup#2 and fn:function-lookup#1 must capture the static context
        // (static base URI, focus) at the point where the named reference is created,
        // so that a later invocation via the reference uses that captured context when
        // resolving the target function and constructing the returned context-dependent
        // function item. Without this, a reference escaping its defining module loses
        // the module's base URI.
        if ((Name.Namespace == FunctionNamespaces.Fn || Name.Prefix == "fn" || Name.Prefix == null)
            && Name.LocalName == "function-lookup")
        {
            object? capturedItem;
            int capturedPos = 1, capturedSize = 1;
            try
            {
                capturedItem = context.ContextItem;
                capturedPos = context.Position;
                capturedSize = context.Last;
            }
            catch (XQueryRuntimeException) { capturedItem = QueryExecutionContext.AbsentFocus; }
            yield return new ContextBoundFunctionRef(func, capturedItem, context.StaticBaseUri, capturedPos, capturedSize);
            yield break;
        }

        // A named function reference always has a fixed arity. Wrap variadic functions so
        // they expose the requested arity (and IsVariadic=false) regardless of whether the
        // requested arity equals the variadic minimum.
        if (func.IsVariadic)
            yield return new VariadicFunctionRefItem(func, Arity);
        else
            yield return func;
    }

    internal static bool IsContextCaptureFunction(QName name)
    {
        if (name.Namespace != FunctionNamespaces.Fn && name.Prefix != "fn" && name.Prefix != null)
            return false;
        return name.LocalName is "name" or "local-name" or "namespace-uri" or "node-name"
            or "string" or "data" or "number" or "normalize-space" or "string-length"
            or "base-uri" or "document-uri" or "nilled"
            or "root" or "path" or "generate-id" or "has-children" or "position" or "last"
            or "static-base-uri"
            // Arity-1 functions that use context node implicitly (e.g., fn:lang#1, fn:id#1, fn:element-with-id#1)
            or "lang" or "id" or "idref" or "element-with-id";
    }
}
