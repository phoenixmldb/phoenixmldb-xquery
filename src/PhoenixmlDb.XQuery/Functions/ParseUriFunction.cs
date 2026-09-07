using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:parse-uri($uri as xs:string) as map(xs:string, xs:string)
/// Decomposes a URI into its components (XPath 4.0).
/// </summary>
public sealed class ParseUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "uri"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var uriStr = arguments[0]?.ToString() ?? "";
        var result = new OrderedXdmMap(XdmMapKeyComparer.Instance);

        if (Uri.TryCreate(uriStr, UriKind.RelativeOrAbsolute, out var uri) && uri.IsAbsoluteUri)
        {
            result["scheme"] = uri.Scheme;
            if (!string.IsNullOrEmpty(uri.UserInfo)) result["userinfo"] = uri.UserInfo;
            result["host"] = uri.Host;
            if (uri.Port >= 0 && !uri.IsDefaultPort) result["port"] = (long)uri.Port;
            result["path"] = uri.AbsolutePath;
            if (!string.IsNullOrEmpty(uri.Query))
                result["query"] = uri.Query.TrimStart('?');
            if (!string.IsNullOrEmpty(uri.Fragment))
                result["fragment"] = uri.Fragment.TrimStart('#');
        }
        else
        {
            result["path"] = uriStr;
        }

        return ValueTask.FromResult<object?>(result);
    }
}
