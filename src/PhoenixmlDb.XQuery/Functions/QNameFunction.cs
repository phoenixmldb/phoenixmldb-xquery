using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:QName($paramURI, $paramQName) as xs:QName
/// </summary>
public sealed class QNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "QName");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.QName, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "paramURI"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "paramQName"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // XPTY0004: arguments must be strings (or untypedAtomic), not numeric etc.
        //
        // XsTypedString counts: it carries the xs:string SUBTYPES — normalizedString, token,
        // language, Name, NCName and below — which the function conversion rules accept
        // wherever xs:string is declared. Rejecting it made fn:QName(…, xs:NCName(…)) fail,
        // and an NCName is the natural thing to build a QName from. ToString() yields the
        // lexical value, so the conversions below need no change. Several other functions
        // (fn:concat, the numeric family) already unwrap it the same way.
        //
        // XsAnyUri counts for the same reason, by a different rule: the function conversion
        // rules include URI PROMOTION — "a value of type xs:anyURI can be promoted to
        // xs:string" — so xs:anyURI is valid wherever xs:string is declared. The engine
        // already honours this everywhere the conversion machinery runs (fn:concat,
        // fn:string-length, fn:upper-case, fn:contains all accept an xs:anyURI); fn:QName
        // hand-rolls its type check instead and so had to be told separately. A namespace
        // URI is the single most natural thing to hold in an xs:anyURI, which made
        // QName(xs:anyURI(...), ...) fail in exactly the case the function exists for.
        var rawUri = Execution.QueryExecutionContext.Atomize(arguments[0]);
        var rawQName = Execution.QueryExecutionContext.Atomize(arguments[1]);
        if (rawQName is not null and not string and not Xdm.XsUntypedAtomic
            and not Xdm.XsTypedString and not Xdm.XsAnyUri)
            throw new XQueryRuntimeException("XPTY0004",
                $"fn:QName second argument must be a string, got {rawQName.GetType().Name}");
        if (rawUri is not null and not string and not Xdm.XsUntypedAtomic
            and not Xdm.XsTypedString and not Xdm.XsAnyUri)
            throw new XQueryRuntimeException("XPTY0004",
                $"fn:QName first argument must be a string, got {rawUri.GetType().Name}");
        var nsUri = rawUri?.ToString() ?? "";
        var qname = rawQName?.ToString() ?? "";

        var colonIdx = qname.IndexOf(':', StringComparison.Ordinal);
        string? prefix = null;
        string localName;
        if (colonIdx > 0)
        {
            prefix = qname[..colonIdx];
            localName = qname[(colonIdx + 1)..];
        }
        else
        {
            localName = qname;
        }

        // FOCA0002: validate lexical form of QName
        static bool IsNCName(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            try { System.Xml.XmlConvert.VerifyNCName(s); return true; }
            catch { return false; }
        }
        if (prefix != null && !IsNCName(prefix))
            throw new XQueryRuntimeException("FOCA0002", $"Invalid QName prefix: '{prefix}'");
        if (!IsNCName(localName))
            throw new XQueryRuntimeException("FOCA0002", $"Invalid QName local name: '{localName}'");
        // FOCA0002: if a prefix is present, the namespace URI must not be empty
        if (prefix != null && string.IsNullOrEmpty(nsUri))
            throw new XQueryRuntimeException("FOCA0002", "Prefix supplied but namespace URI is empty");

        var nsId = string.IsNullOrEmpty(nsUri) ? NamespaceId.None : new NamespaceId((uint)Math.Abs(nsUri.GetHashCode()));
        // When URI is explicitly empty (""), set RuntimeNamespace to "" so that
        // fn:function-lookup can distinguish "explicitly no namespace" from "namespace unknown".
        var result = new QName(nsId, localName, prefix) { RuntimeNamespace = nsUri.Length == 0 ? "" : nsUri };
        return ValueTask.FromResult<object?>(result);
    }
}
