using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:doc-available($uri) as xs:boolean
/// </summary>
public sealed class DocAvailableFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "doc-available");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "uri"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        if (arg == null)
            return ValueTask.FromResult<object?>(false);
        // Per XPath 3.1 function conversion rules, xs:untypedAtomic is castable to xs:string;
        // doc-available's signature accepts xs:string?, so we cast UA → string transparently
        // rather than raising XPTY0004. Without this, expressions like
        // <xsl:param name="uri" select="xs:untypedAtomic('foo.xml')"/> followed by
        // doc-available($uri) fail spuriously.
        var uri = arg switch
        {
            string s => s,
            Xdm.XsAnyUri u => u.Value,
            Xdm.XsUntypedAtomic ua => ua.Value,
            _ => throw context.Error("XPTY0004", $"Expected xs:string for fn:doc-available, got {arg.GetType().Name}")
        };

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            if (queryContext.StaticBaseUri != null)
            {
                if (uri.Length == 0)
                    uri = queryContext.StaticBaseUri;
                else if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
                {
                    if (Uri.TryCreate(queryContext.StaticBaseUri, UriKind.Absolute, out var baseUri))
                        uri = new Uri(baseUri, uri).AbsoluteUri;
                }
            }
            uri = ResourceUriResolver.Map(queryContext, uri);
            var available = queryContext.DocumentResolver.IsDocumentAvailable(uri);
            return ValueTask.FromResult<object?>(available);
        }

        return ValueTask.FromResult<object?>(false);
    }
}
