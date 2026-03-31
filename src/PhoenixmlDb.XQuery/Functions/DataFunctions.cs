using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:data($arg) as xs:anyAtomicType*
/// </summary>
public sealed class DataFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "data");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        // XQuery 3.1: atomizing an array concatenates the atomized members
        if (arg is List<object?> array)
        {
            var results = new List<object?>();
            foreach (var member in array)
            {
                var atomized = Atomize(member);
                if (atomized is object?[] seq)
                    results.AddRange(seq);
                else if (atomized != null)
                    results.Add(atomized);
            }
            return ValueTask.FromResult<object?>(results.Count == 0 ? null
                : results.Count == 1 ? results[0] : results.ToArray());
        }

        // FOTY0013: maps/functions are not atomizable
        if (arg is IDictionary<object, object?> or XQueryFunction)
            return ValueTask.FromResult(Atomize(arg)); // will throw FOTY0013

        if (arg is IEnumerable<object?> seq)
        {
            var results = seq.Select(Atomize).ToArray();
            return ValueTask.FromResult<object?>(results);
        }

        return ValueTask.FromResult(Atomize(arg));
    }

    internal static object? Atomize(object? item) => Atomize(item, null);

    internal static object? Atomize(object? item, INodeProvider? nodeProvider)
    {
        // Per XDM spec: typed value of untyped nodes (element, attribute, text, document)
        // is xs:untypedAtomic. PI and comment typed values are xs:string.
        // FOTY0013: Atomization is not defined for function items (maps, arrays, functions).
        return item switch
        {
            null => null,
            XdmElement elem => new XsUntypedAtomic(
                Execution.QueryExecutionContext.ComputeElementStringValue(elem, nodeProvider)),
            XdmAttribute attr => new XsUntypedAtomic(attr.Value),
            XdmText text => new XsUntypedAtomic(text.Value),
            XdmComment comment => comment.Value,
            XdmProcessingInstruction pi => pi.Value,
            XdmDocument doc => new XsUntypedAtomic(
                Execution.QueryExecutionContext.ComputeDocumentStringValue(doc, nodeProvider)),
            // System.Xml DOM nodes (used by XSLT engine)
            System.Xml.XmlElement xmlElem => new XsUntypedAtomic(xmlElem.InnerText),
            System.Xml.XmlAttribute xmlAttr => new XsUntypedAtomic(xmlAttr.Value),
            System.Xml.XmlText xmlText => new XsUntypedAtomic(xmlText.Value ?? ""),
            System.Xml.XmlComment xmlComment => xmlComment.Value ?? "",
            System.Xml.XmlProcessingInstruction xmlPi => xmlPi.Value ?? "",
            System.Xml.XmlDocument xmlDoc => new XsUntypedAtomic(xmlDoc.InnerText ?? ""),
            // LINQ XML nodes (from fn:parse-xml, etc.)
            System.Xml.Linq.XElement linqElem => new XsUntypedAtomic(linqElem.Value),
            System.Xml.Linq.XAttribute linqAttr => new XsUntypedAtomic(linqAttr.Value),
            System.Xml.Linq.XText linqText => new XsUntypedAtomic(linqText.Value),
            System.Xml.Linq.XComment linqComment => linqComment.Value,
            System.Xml.Linq.XProcessingInstruction linqPi => linqPi.Data,
            System.Xml.Linq.XDocument linqDoc => new XsUntypedAtomic(linqDoc.Root?.Value ?? ""),
            IDictionary<object, object?> => throw new XQueryException("FOTY0013", "Atomization is not defined for maps"),
            List<object?> => throw new XQueryException("FOTY0013", "Atomization is not defined for arrays"),
            XQueryFunction => throw new XQueryException("FOTY0013", "Atomization is not defined for function items"),
            _ => item // Already atomic
        };
    }
}

/// <summary>
/// fn:data() as xs:anyAtomicType* (uses context item)
/// </summary>
public sealed class Data0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "data");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var contextItem = context.ContextItem;
        if (contextItem == null)
            throw new XQueryException("XPDY0002", "Context item is absent for fn:data()");
        return ValueTask.FromResult(DataFunction.Atomize(contextItem));
    }
}
