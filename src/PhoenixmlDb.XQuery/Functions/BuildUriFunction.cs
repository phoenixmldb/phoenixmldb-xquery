using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:build-uri($components as map(xs:string, item()*)) as xs:string
/// Constructs a URI from component parts (XPath 4.0).
/// </summary>
public sealed class BuildUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "build-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "components"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not IDictionary<object, object?> map)
            return ValueTask.FromResult<object?>("");

        var sb = new System.Text.StringBuilder();

        if (MapKeyHelper.TryGetValue(map, "scheme", out var scheme) && scheme != null)
        {
            sb.Append(scheme).Append("://");
            if (MapKeyHelper.TryGetValue(map, "userinfo", out var userinfo) && userinfo != null)
                sb.Append(userinfo).Append('@');
            if (MapKeyHelper.TryGetValue(map, "host", out var host) && host != null)
                sb.Append(host);
            if (MapKeyHelper.TryGetValue(map, "port", out var port) && port != null)
                sb.Append(':').Append(port);
        }

        if (MapKeyHelper.TryGetValue(map, "path", out var path) && path != null)
            sb.Append(path);
        if (MapKeyHelper.TryGetValue(map, "query", out var query) && query != null)
            sb.Append('?').Append(query);
        if (MapKeyHelper.TryGetValue(map, "fragment", out var fragment) && fragment != null)
            sb.Append('#').Append(fragment);

        return ValueTask.FromResult<object?>(sb.ToString());
    }
}
