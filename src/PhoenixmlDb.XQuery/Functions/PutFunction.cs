using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// <c>fn:put($node, $uri)</c> and <c>fn:put($node, $uri, $params)</c> (XQUF 3.0 §6.1): an updating
/// function that stores a document or element at a URI when the query's updates are applied.
/// </summary>
/// <remarks>
/// <para>
/// The write is a resource access like any read: it needs a <see cref="Security.ResourcePolicy"/>
/// that allows <see cref="Security.ResourceAccessKind.WriteDocument"/> for the resolved URI. With
/// no policy fn:put is refused (Lucas, 2026-11-20): a host that runs queries does not get file
/// writes by accident. A refusal is <c>phx:XUDY9001</c>, as for an update target that cannot be
/// changed, and nothing is changed.
/// </para>
/// <para>
/// Registered for XQuery queries only (<see cref="QueryEngine"/>), not in
/// <see cref="FunctionLibrary.Standard"/>, which the XSLT engine copies.
/// </para>
/// </remarks>
internal sealed class PutFunction(int arity) : XQueryFunction
{
    internal static QName PutName { get; } = new(FunctionNamespaces.Fn, "put");

    /// <summary>Adds fn:put with two and three arguments to a library that has neither.</summary>
    internal static FunctionLibrary AddTo(FunctionLibrary library)
    {
        if (library.Resolve(PutName, 2) is null)
            library.Register(new PutFunction(2));
        if (library.Resolve(PutName, 3) is null)
            library.Register(new PutFunction(3));
        return library;
    }

    public override QName Name => PutName;
    internal override bool IsUpdating => true;
    public override XdmSequenceType ReturnType => XdmSequenceType.Empty;
    public override IReadOnlyList<FunctionParameterDef> Parameters => arity == 2
        ? [new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.Node },
           new() { Name = new QName(NamespaceId.None, "uri"), Type = XdmSequenceType.String }]
        : [new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.Node },
           new() { Name = new QName(NamespaceId.None, "uri"), Type = XdmSequenceType.String },
           new() { Name = new QName(NamespaceId.None, "params"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not (XdmDocument or XdmElement))
            throw new XQueryRuntimeException("FOUP0001",
                "fn:put stores a document or an element node, not "
                + (arguments[0] is XdmNode node ? $"a {node.NodeKind} node" : "an atomic value") + ".");

        var href = arguments[1]?.ToString() ?? "";
        if (!Uri.TryCreate(href, UriKind.RelativeOrAbsolute, out _))
            throw new XQueryRuntimeException("FOUP0002", $"fn:put: '{href}' is not a valid URI.");

        if (context is not QueryExecutionContext query)
            throw new XQueryRuntimeException("XUST0002", "fn:put is an updating function; it cannot run here.");

        // The write is authorised now, against the URI resolved with the static base URI; the
        // file is written when the query's updates are applied, and checked again then.
        if (context.ResourcePolicy is not { } policy)
            throw Refused($"fn:put to '{href}' is not allowed: no resource policy allows writing (the host must give one that "
                + "allows WriteDocument for it; the command line: --allow-write <directory>). Nothing was changed.");
        var baseUri = context.StaticBaseUri is { } b && Uri.TryCreate(b, UriKind.Absolute, out var parsed) ? parsed : null;
        Uri target;
        try
        {
            target = policy.Authorize(href, Security.ResourceAccessKind.WriteDocument, baseUri);
        }
        catch (Security.ResourceAccessDeniedException e)
        {
            throw Refused($"fn:put: {e.Message}. Nothing was changed.", e);
        }

        query.PendingUpdates.Add(new PutPrimitive
        {
            Target = arguments[0]!,
            Uri = target,
            Parameters = arity == 3 ? arguments[2] : null,
        });
        return ValueTask.FromResult<object?>(null);
    }

    internal static XQueryRuntimeException Refused(string message, Exception? inner = null)
        => inner is null
            ? new XQueryRuntimeException("XUDY9001", message) { ErrorNamespaceUri = PendingUpdateApplicator.PhxErrorNamespace, ErrorPrefix = "phx" }
            : new XQueryRuntimeException("XUDY9001", message, inner) { ErrorNamespaceUri = PendingUpdateApplicator.PhxErrorNamespace, ErrorPrefix = "phx" };
}
