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
/// A function that the host can turn off for an execution. Turned off, it is not there to be
/// found: <c>function-lookup</c> and <c>function-available</c> do not report it.
/// </summary>
public interface IHostGatedFunction
{
    /// <summary>True when the host has turned the function off for <paramref name="context"/>.</summary>
    bool IsTurnedOff(Ast.ExecutionContext? context);
}

/// <summary>
/// fn:transform($options as map(*)) as map(*)
/// Runs an XSLT transformation and returns the result as a map.
/// Delegates to <see cref="ITransformProvider"/> — set <see cref="Provider"/>
/// before use or the function will throw FOXT0001.
/// </summary>
public sealed class TransformFunction : XQueryFunction, IHostGatedFunction
{
    /// <inheritdoc/>
    public bool IsTurnedOff(Ast.ExecutionContext? context) => IsDisallowed(context);

    /// <summary>
    /// The XSLT transform provider. Set this before executing queries that use fn:transform().
    /// Typically set by the XSLT layer at initialization.
    /// </summary>
    public static ITransformProvider? Provider { get; set; }

    /// <summary>
    /// True when the resource policy of <paramref name="context"/> turns fn:transform off
    /// (<see cref="Security.ResourcePolicy.AllowTransformFunction"/>).
    /// </summary>
    public static bool IsDisallowed(Ast.ExecutionContext? context)
        => context?.ResourcePolicy is { AllowTransformFunction: false };

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
        // Before anything is read from the options: every way to call the function (by name,
        // through a function item from a named reference, function-lookup or partial
        // application, through fn:apply) arrives here.
        if (IsDisallowed(context))
            throw context.Error("FOXT0001", "fn:transform is not available: the host's resource policy does not allow it");

        if (arguments[0] is not IDictionary<object, object?> options)
            throw context.Error("FOXT0001", "The argument to fn:transform must be a map");

        var provider = Provider;
        if (provider == null)
            throw context.Error("FOXT0001",
                "fn:transform is not available — no XSLT processor has been registered. " +
                "Add a reference to PhoenixmlDb.Xslt and call TransformFunction.Provider = new XsltTransformProvider().");

        return provider.TransformAsync(options, context);
    }
}
