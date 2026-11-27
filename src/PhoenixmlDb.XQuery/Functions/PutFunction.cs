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
/// The write is authorised when the list is applied (<see cref="PutPrimitive"/>). Without a host
/// resolver (<see cref="Security.ResourcePolicy.ResourceResolver"/>) only the library can write
/// it, so it is also authorised now, and a refusal stops the query before any update is applied.
/// With one, the host may take the write (<see cref="Security.IResourceResolver.OpenResultDocument"/>)
/// to a URI the policy decides on then; the URI is only resolved now.
/// </para>
/// <para>
/// When the host applies the list itself (<see cref="QueryExecutionContext.DeferPendingUpdates"/>),
/// nothing is authorised now, with or without a policy: the URI is resolved against the static
/// base URI, the parameters are checked, and the write is authorised by the policy and written
/// through the resolver of the context that applies the list
/// (<see cref="PendingUpdateApplicator.ApplyUpdates"/>), which refuses it without a policy. A host
/// such as a database runs its queries with no policy and applies their lists with one.
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

        var baseUri = context.StaticBaseUri is { } b && Uri.TryCreate(b, UriKind.Absolute, out var parsed) ? parsed : null;
        Uri target;
        if (query.DeferPendingUpdates)
        {
            // The host applies the list: the policy and the resolver of the context that applies it
            // authorise and take the write (ApplyUpdates refuses it without a policy). The URI is
            // only resolved now.
            target = Security.ResourcePolicy.Resolve(href, baseUri)
                ?? throw new XQueryRuntimeException("FOUP0002", $"fn:put: '{href}' is not a valid URI.");
        }
        // Without a host resolver the write is authorised now, against the URI resolved with the
        // static base URI, and again when the list is applied. With one, the host may take it, and
        // it is authorised then.
        else if (context.ResourcePolicy is not { } policy)
        {
            throw Refused($"fn:put to '{href}' is not allowed: no resource policy allows writing (the host must give one that "
                + "allows WriteDocument for it; the command line: --allow-write <directory>). Nothing was changed.");
        }
        else if (policy.ResourceResolver is not null)
        {
            target = Security.ResourcePolicy.Resolve(href, baseUri)
                ?? throw new XQueryRuntimeException("FOUP0002", $"fn:put: '{href}' is not a valid URI.");
        }
        else
        {
            try
            {
                target = policy.Authorize(href, Security.ResourceAccessKind.WriteDocument, baseUri);
            }
            catch (Security.ResourceAccessDeniedException e)
            {
                throw Refused($"fn:put: {e.Message}. Nothing was changed.", e);
            }
        }

        // The serialization parameters are read and checked now, with the rules of fn:serialize, so
        // an invalid one stops the query before any update is applied, and the context that applies
        // the list need not read the element.
        var (parameters, parsedParameters, method) = arity == 3 ? ReadParameters(arguments[2], context) : (null, null, "xml");

        query.PendingUpdates.Add(new PutPrimitive
        {
            Target = arguments[0]!,
            Uri = target,
            Parameters = parameters,
            ParsedParameters = parsedParameters,
            Method = method,
        });
        return ValueTask.FromResult<object?>(null);
    }

    /// <summary>
    /// The third argument of fn:put, <c>element(output:serialization-parameters)?</c> (XQUF 3.0
    /// §6.1): XPTY0004 for another item; SEPM0016, SEPM0017 and the other errors of fn:serialize
    /// for an invalid parameter. The method is that fn:serialize would use: xml when none is given.
    /// </summary>
    private static (XdmElement? Element, IDictionary<object, object?>? Parsed, string Method) ReadParameters(object? argument, Ast.ExecutionContext context)
    {
        // A dynamic call gives an empty sequence as an empty array; a call gives one item as the item.
        if (argument is object?[] { Length: 0 })
            argument = null;
        if (argument is null)
            return (null, null, "xml");
        if (argument is not XdmElement element)
            throw new XQueryRuntimeException("XPTY0004",
                "fn:put: the third argument must be an output:serialization-parameters element or the empty sequence.");
        var parsed = Serialize2Function.ParseParametersElement(element, context);
        var options = Serialize2Function.EffectiveOptions(parsed, paramsFromMap: false);
        return (element, parsed, MethodName(options.Method));
    }

    private static string MethodName(OutputMethod method) => method switch
    {
        OutputMethod.Xml => "xml",
        OutputMethod.Xhtml => "xhtml",
        OutputMethod.Html => "html",
        OutputMethod.Text => "text",
        OutputMethod.Json => "json",
        _ => "adaptive",
    };

    internal static XQueryRuntimeException Refused(string message, Exception? inner = null)
        => inner is null
            ? new XQueryRuntimeException("XUDY9001", message) { ErrorNamespaceUri = PendingUpdateApplicator.PhxErrorNamespace, ErrorPrefix = "phx" }
            : new XQueryRuntimeException("XUDY9001", message, inner) { ErrorNamespaceUri = PendingUpdateApplicator.PhxErrorNamespace, ErrorPrefix = "phx" };
}
