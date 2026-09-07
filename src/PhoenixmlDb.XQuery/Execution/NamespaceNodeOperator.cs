using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Processing instruction constructor operator — creates an XdmProcessingInstruction node.
/// Used for processing-instruction name { "content" } and &lt;?target content?&gt; expressions.
/// </summary>
/// <summary>
/// Computed namespace constructor: namespace prefix { "uri" }
/// Yields an XdmNamespace node that the parent ElementConstructor collects.
/// </summary>
public sealed class NamespaceNodeOperator : PhysicalOperator
{
    public string? DirectPrefix { get; init; }
    public PhysicalOperator? PrefixOperator { get; init; }
    public required PhysicalOperator UriOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        string prefix;
        if (DirectPrefix != null)
            prefix = DirectPrefix;
        else if (PrefixOperator != null)
        {
            prefix = "";
            await foreach (var p in PrefixOperator.ExecuteAsync(context))
            {
                var atomized = QueryExecutionContext.Atomize(p);
                // XPTY0004: the prefix expression must yield xs:string, xs:untypedAtomic,
                // or xs:NCName. Other atomic types (e.g. xs:anyURI, xs:duration) are errors.
                if (atomized is not null
                    and not string
                    and not PhoenixmlDb.Xdm.XsUntypedAtomic)
                {
                    throw new XQueryRuntimeException("XPTY0004",
                        $"Namespace constructor: prefix expression must be xs:string or xs:untypedAtomic, got {atomized.GetType().Name}");
                }
                prefix = atomized?.ToString()?.Trim() ?? "";
                break;
            }
        }
        else
            prefix = "";

        // XQDY0074: if prefix is non-empty it must be a valid NCName
        if (!string.IsNullOrEmpty(prefix))
        {
            try { System.Xml.XmlConvert.VerifyNCName(prefix); }
            catch
            {
                throw new XQueryRuntimeException("XQDY0074",
                    $"Namespace constructor: prefix '{prefix}' is not a valid NCName");
            }
        }

        var sb = new StringBuilder();
        await foreach (var item in UriOperator.ExecuteAsync(context))
        {
            var atomized = QueryExecutionContext.Atomize(item);
            if (atomized != null)
                sb.Append(atomized.ToString());
        }
        var uri = sb.ToString();

        // XQDY0101: cannot bind 'xml'/'xmlns' prefixes, and cannot bind any prefix
        // to the reserved XML/XMLNS namespaces. An empty prefix bound to the empty URI
        // is allowed (default namespace undeclaration). A non-empty prefix with an
        // empty URI is also forbidden.
        if (prefix == "xmlns")
            throw new XQueryRuntimeException("XQDY0101",
                "Namespace constructor: the 'xmlns' prefix cannot be declared");
        if (prefix == "xml" && uri != "http://www.w3.org/XML/1998/namespace")
            throw new XQueryRuntimeException("XQDY0101",
                "Namespace constructor: the 'xml' prefix can only be bound to the XML namespace");
        if (prefix != "xml" && uri == "http://www.w3.org/XML/1998/namespace")
            throw new XQueryRuntimeException("XQDY0101",
                "Namespace constructor: the XML namespace can only be bound to the 'xml' prefix");
        if (uri == "http://www.w3.org/2000/xmlns/")
            throw new XQueryRuntimeException("XQDY0101",
                "Namespace constructor: the XMLNS namespace cannot be bound to any prefix");
        if (string.IsNullOrEmpty(uri) && !string.IsNullOrEmpty(prefix))
            throw new XQueryRuntimeException("XQDY0101",
                $"Namespace constructor: prefix '{prefix}' cannot be bound to the empty URI");

        // Allocate a fresh NodeId per invocation so node identity (`is`) treats
        // each call to a namespace constructor as a distinct node — required by
        // QT3 nscons-028 ("$ns is mod1:one()" must be false even when the
        // function body is the same constructor expression).
        var store = context.NodeStore as INodeBuilder;
        var nsId = store?.AllocateId() ?? new NodeId(0);
        var nsNode = new XdmNamespace
        {
            Id = nsId,
            Document = new DocumentId(0),
            Prefix = prefix,
            Uri = uri
        };
        yield return nsNode;
    }
}
