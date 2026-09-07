using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:function-name($func as function(*)) as xs:QName?
/// </summary>
public sealed class FunctionNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "function-name");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "func"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is XQueryFunction func)
        {
            // Anonymous functions (inline functions, closures) have no name per XPath spec
            if (func.IsAnonymous)
                return ValueTask.FromResult<object?>(null);
            var name = func.Name;
            // Synthesize a conventional prefix for well-known namespaces if missing,
            // so the returned QName round-trips through serialization as e.g. "fn:function-name".
            string? effectivePrefix = name.Prefix;
            if (effectivePrefix == null && name.Namespace != NamespaceId.None)
            {
                if (name.Namespace == FunctionNamespaces.Fn) effectivePrefix = "fn";
                else if (name.Namespace == FunctionNamespaces.Xs) effectivePrefix = "xs";
                else if (name.Namespace == FunctionNamespaces.Math) effectivePrefix = "math";
                else if (name.Namespace == FunctionNamespaces.Map) effectivePrefix = "map";
                else if (name.Namespace == FunctionNamespaces.Array) effectivePrefix = "array";
                else if (name.Namespace == FunctionNamespaces.Local) effectivePrefix = "local";
            }
            // Ensure the namespace URI is resolvable for namespace-uri-from-QName()
            string? nsUri = null;
            if (name.ResolvedNamespace == null && name.Namespace != NamespaceId.None)
            {
                nsUri = FunctionNamespaces.ResolveNamespace(name.Namespace);
                if (nsUri == null)
                {
                    var resolver = (context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext)?.NamespaceResolver;
                    nsUri = resolver?.Invoke(name.Namespace);
                }
            }
            if (effectivePrefix != name.Prefix || nsUri != null)
            {
                name = new QName(name.Namespace, name.LocalName, effectivePrefix)
                {
                    RuntimeNamespace = nsUri ?? name.RuntimeNamespace
                };
            }
            return ValueTask.FromResult<object?>(name);
        }
        // Maps and arrays are also callable (function items per XPath 3.1)
        if (arguments[0] is IDictionary<object, object?> || arguments[0] is List<object?>)
            return ValueTask.FromResult<object?>(null); // anonymous
        throw new Execution.XQueryRuntimeException("XPTY0004",
            $"Argument to fn:function-name is not a function (got {arguments[0]?.GetType().Name ?? "empty sequence"})");
    }
}
