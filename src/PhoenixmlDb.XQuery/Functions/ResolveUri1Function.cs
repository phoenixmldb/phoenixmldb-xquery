using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:resolve-uri($relative) as xs:anyURI?
/// </summary>
public sealed class ResolveUri1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "resolve-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalAnyUri;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "relative"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var relative = arguments[0]?.ToString();
        if (relative == null)
            return ValueTask.FromResult<object?>(null);

        var baseUri = context.StaticBaseUri ?? "";

        // FORG0002: reject malformed percent-escapes (e.g. "%gg")
        static bool HasBadPercentEscape(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '%')
                {
                    if (i + 2 >= s.Length) return true;
                    if (!Uri.IsHexDigit(s[i + 1]) || !Uri.IsHexDigit(s[i + 2])) return true;
                    i += 2;
                }
            }
            return false;
        }
        if (HasBadPercentEscape(relative))
            throw new XQueryRuntimeException("FORG0002", $"The relative URI '{relative}' contains invalid percent-encoding");

        try
        {
            if (Uri.TryCreate(baseUri, UriKind.Absolute, out var baseUriObj) &&
                Uri.TryCreate(baseUriObj, relative, out var resolved))
            {
                return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(resolved.OriginalString));
            }
            if (Uri.TryCreate(relative, UriKind.Absolute, out _))
                return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(relative));
            return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(relative));
        }
        catch (UriFormatException)
        {
            // URI resolution failed — return the relative URI unchanged per spec
            return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(relative));
        }
    }
}
