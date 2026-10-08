using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// The constructor function of a simple type an imported schema declares (XQuery 3.1 §3.18.4):
/// <c>T($arg as xs:anyAtomicType?) as T?</c>, equivalent to <c>$arg cast as T?</c>.
/// Registered for each global simple type when the schema is imported.
/// </summary>
public sealed class SchemaTypeConstructorFunction : XQueryFunction
{
    private readonly string _namespaceUri;
    private readonly string _localName;

    public SchemaTypeConstructorFunction(QName name, string namespaceUri, string localName)
    {
        Name = name;
        _namespaceUri = namespaceUri;
        _localName = localName;
    }

    /// <summary>
    /// The constructor for <paramref name="name"/>#<paramref name="arity"/> if that names a simple
    /// type an imported schema declares; for the dynamic lookups (<c>T#1</c>, fn:function-lookup),
    /// which resolve against the runtime library rather than the compile-time one.
    /// </summary>
    internal static SchemaTypeConstructorFunction? TryCreate(QName name, int arity, QueryExecutionContext context)
    {
        if (arity != 1 || context.SchemaProvider is not { } provider)
            return null;
        // The last source is for a host that keeps its namespaces in the function library
        // (a stylesheet's are registered there) and gives the context no resolver.
        var uri = name.RuntimeNamespace ?? name.ExpandedNamespace ?? context.NamespaceResolver?.Invoke(name.Namespace)
            ?? context.Functions.RegisteredNamespaceUri(name.Namespace);
        if (string.IsNullOrEmpty(uri) && name.Namespace == NamespaceId.None && !string.IsNullOrEmpty(name.Prefix)
            && context.PrefixNamespaceBindings is { } bindings && bindings.TryGetValue(name.Prefix, out var bound))
            uri = bound;
        if (string.IsNullOrEmpty(uri) || uri == "http://www.w3.org/2001/XMLSchema"
            || provider.GetSchemaSimpleType(uri, name.LocalName) is null)
            return null;
        return new SchemaTypeConstructorFunction(name, uri, name.LocalName);
    }

    public override QName Name { get; }
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalAnyAtomicType;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalAnyAtomicType }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0] switch
        {
            object?[] { Length: 0 } => null,
            object?[] { Length: 1 } single => single[0],
            object?[] => throw new XQueryRuntimeException("XPTY0004",
                $"The constructor function for Q{{{_namespaceUri}}}{_localName} takes at most one item"),
            var one => one,
        };
        arg = QueryExecutionContext.AtomizeTyped(arg);
        if (arg is null)
            return ValueTask.FromResult<object?>(null);
        var provider = (context as QueryExecutionContext)?.SchemaProvider
            ?? throw new XQueryRuntimeException("XPST0051",
                $"Q{{{_namespaceUri}}}{_localName} is a schema-defined type, but no schema provider is registered.");
        return ValueTask.FromResult(TypeCastHelper.CastToSchemaSimpleType(arg, _namespaceUri, _localName, provider, context as QueryExecutionContext));
    }
}
