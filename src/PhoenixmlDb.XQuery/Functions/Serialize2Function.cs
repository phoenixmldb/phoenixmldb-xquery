using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:serialize($arg, $params) as xs:string
/// </summary>
public sealed class Serialize2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "serialize");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrMore } },
        new() { Name = new QName(NamespaceId.None, "params"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];

        var paramsArg = arguments.Count > 1 ? arguments[1] : null;
        if (paramsArg is object?[] emptyParamsArr && emptyParamsArr.Length == 0) paramsArg = null;
        var paramsMap = paramsArg as IDictionary<object, object?>;
        bool paramsFromMap = paramsMap != null;

        if (paramsMap == null && paramsArg is Xdm.Nodes.XdmElement paramsElem)
        {
            var nodeProv = (context as QueryExecutionContext)?.NodeProvider;
            string? nsUri = null;
            if (nodeProv is XdmDocumentStore paramsStore)
                nsUri = paramsStore.ResolveNamespaceUri(paramsElem.Namespace)?.ToString();
            if (nsUri != "http://www.w3.org/2010/xslt-xquery-serialization"
                || paramsElem.LocalName != "serialization-parameters")
                throw new XQueryRuntimeException("XPTY0004",
                    "serialization parameters element must be <output:serialization-parameters>");
            // Delegate to the comprehensive validation in XQueryResultSerializer
            paramsMap = XQueryResultSerializer.ParseSerializationParamsElement(paramsElem, nodeProv);
        }

        // Delegate to the comprehensive option parsing in XQueryResultSerializer
        var options = XQueryResultSerializer.ParseSerializationOptions(paramsMap, paramsFromMap);
        var method = options.Method;

        // In adaptive mode, attributes ARE allowed at top level
        if (method != OutputMethod.Adaptive)
        {
            SerializeFunction.CheckSerr0001(arg);
            if (arg is IEnumerable<object?> argSeq && arg is not string)
                foreach (var it in argSeq) SerializeFunction.CheckSerr0001(it);
        }

        // JSON: empty sequence => "null"; SERE0023 for multi-item sequences
        if (method == OutputMethod.Json)
        {
            if (arg == null || (arg is object?[] nullArr && nullArr.Length == 0))
                return ValueTask.FromResult<object?>("null");
            if (arg is object?[] jsonArr && jsonArr.Length > 1)
                throw new XQueryRuntimeException("SERE0023",
                    "JSON output method cannot serialize a sequence of more than one item");
            if (arg is IEnumerable<object?> jsonSeq && arg is not string
                && arg is not IDictionary<object, object?> && arg is not List<object?>)
            {
                var count = 0;
                foreach (var _ in jsonSeq) { count++; if (count > 1) break; }
                if (count > 1)
                    throw new XQueryRuntimeException("SERE0023",
                        "JSON output method cannot serialize a sequence of more than one item");
            }
        }

        if (arg == null)
        {
            if (method == OutputMethod.Json)
                return ValueTask.FromResult<object?>("null");
            return ValueTask.FromResult<object?>("");
        }

        if (context is Execution.QueryExecutionContext qec && qec.NodeProvider is XdmDocumentStore store)
        {
            var serializer = new XQueryResultSerializer(store, options);
            return ValueTask.FromResult<object?>(serializer.Serialize(arg));
        }

        var nodeProvider = (context as QueryExecutionContext)?.NodeProvider;
        return ValueTask.FromResult<object?>(SerializeFunction.SerializeItem(arg, nodeProvider, method));
    }
}
