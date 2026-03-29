using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Provides XSLT transformation capabilities for fn:transform().
/// Implement this interface in the XSLT layer and register via
/// <see cref="TransformFunction.Provider"/>.
/// </summary>
public interface ITransformProvider
{
    /// <summary>
    /// Executes fn:transform($options) and returns the result map.
    /// </summary>
    /// <param name="options">The options map (stylesheet-location, source-node, etc.).</param>
    /// <param name="context">The XQuery execution context.</param>
    /// <returns>A map with "output" key and optional secondary result document keys.</returns>
    ValueTask<object?> TransformAsync(IDictionary<object, object?> options, Ast.ExecutionContext context);
}

/// <summary>
/// fn:transform($options as map(*)) as map(*)
/// Runs an XSLT transformation and returns the result as a map.
/// Delegates to <see cref="ITransformProvider"/> — set <see cref="Provider"/>
/// before use or the function will throw FOXT0001.
/// </summary>
public sealed class TransformFunction : XQueryFunction
{
    /// <summary>
    /// The XSLT transform provider. Set this before executing queries that use fn:transform().
    /// Typically set by the XSLT layer at initialization.
    /// </summary>
    public static ITransformProvider? Provider { get; set; }

    public override QName Name => new(FunctionNamespaces.Fn, "transform");
    public override XdmSequenceType ReturnType => new()
    {
        ItemType = ItemType.Map,
        Occurrence = Occurrence.ExactlyOne
    };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "options"), Type = new XdmSequenceType
        {
            ItemType = ItemType.Map,
            Occurrence = Occurrence.ExactlyOne
        }}
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is not IDictionary<object, object?> options)
            throw new XQueryException("FOXT0001", "The argument to fn:transform must be a map");

        var provider = Provider;
        if (provider == null)
            throw new XQueryException("FOXT0001",
                "fn:transform is not available — no XSLT processor has been registered. " +
                "Add a reference to PhoenixmlDb.Xslt and call TransformFunction.Provider = new XsltTransformProvider().");

        return provider.TransformAsync(options, context);
    }
}
