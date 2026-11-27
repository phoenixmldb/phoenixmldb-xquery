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
            paramsMap = ParseParametersElement(paramsElem, context);
        // F&O 3.1 §14.7.3: "A type error [err:XPTY0004] occurs if the second argument is present and
        // does not match either of the types element(output:serialization-parameters)? or map(*)." A
        // string or another node was ignored, and the defaults used.
        else if (paramsMap == null && paramsArg is not null)
            throw new XQueryRuntimeException("XPTY0004",
                $"fn:serialize: the second argument must be an output:serialization-parameters element, a map or the empty sequence, not {XdmShape.TypeNameOf(paramsArg)}");

        return SerializeAsync(arg, paramsMap, paramsFromMap, context);
    }

    /// <summary>
    /// An <c>output:serialization-parameters</c> element as a parameter map, read with the
    /// context's node provider: XPTY0004 for another element, SEPM0017 and the other errors of an
    /// invalid one. Shared with <c>fn:put</c>, which reads it when it is called.
    /// </summary>
    internal static IDictionary<object, object?> ParseParametersElement(Xdm.Nodes.XdmElement paramsElem, Ast.ExecutionContext context)
    {
        var nodeProv = (context as QueryExecutionContext)?.NodeProvider;
        string? nsUri = null;
        if (nodeProv is INodeStore paramsStore)
            nsUri = paramsStore.GetNamespaceUri(paramsElem.Namespace);
        if (nsUri != "http://www.w3.org/2010/xslt-xquery-serialization"
            || paramsElem.LocalName != "serialization-parameters")
            throw new XQueryRuntimeException("XPTY0004",
                "serialization parameters element must be <output:serialization-parameters>");
        // Delegate to the comprehensive validation in XQueryResultSerializer
        return XQueryResultSerializer.ParseSerializationParamsElement(paramsElem, nodeProv);
    }

    /// <summary>
    /// The options fn:serialize uses for these parameters (SEPM0016 for an invalid value), with
    /// the defaults it takes where they give none.
    /// </summary>
    internal static SerializationOptions EffectiveOptions(IDictionary<object, object?>? paramsMap, bool paramsFromMap)
    {
        // Delegate to the comprehensive option parsing in XQueryResultSerializer
        var options = XQueryResultSerializer.ParseSerializationOptions(paramsMap, paramsFromMap);
        // F&O 3.1 §14.1.2: a parameter the map leaves out takes its default from the table there,
        // and the method's is xml. The default here was adaptive, so serialize((true(), "a"), map{})
        // gave `true() "a"` (Saxon-HE 12.10: `true a`) and serialize(map{…}, map{}) succeeded.
        // The one-argument form and the serialization-parameters element leave the defaults to the
        // implementation; they take the same xml default, as Saxon does.
        if (paramsMap is null || !paramsMap.ContainsKey("method"))
            options = options with { Method = OutputMethod.Xml, OmitXmlDeclaration = options.OmitXmlDeclaration || paramsMap is null };
        return options;
    }

    /// <summary>fn:serialize with its parameters already read (<see cref="ParseParametersElement"/>).</summary>
    internal static ValueTask<object?> SerializeAsync(object? arg, IDictionary<object, object?>? paramsMap, bool paramsFromMap,
        Ast.ExecutionContext context)
    {
        var options = EffectiveOptions(paramsMap, paramsFromMap);
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

        if (context is Execution.QueryExecutionContext qec && qec.NodeProvider is INodeStore store)
        {
            var serializer = new XQueryResultSerializer(store, options);
            return ValueTask.FromResult<object?>(Execution.StringLengthLimit.Checked(context, serializer.Serialize(arg), "fn:serialize"));
        }

        var nodeProvider = (context as QueryExecutionContext)?.NodeProvider;
        return ValueTask.FromResult<object?>(Execution.StringLengthLimit.Checked(context, SerializeFunction.SerializeItem(arg, nodeProvider, method), "fn:serialize"));
    }
}
