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
/// Wraps an InlineFunctionItem as a registered function with the correct name/arity.
/// </summary>
internal sealed class DeclaredFunction : XQueryFunction
{
    private readonly QName _name;
    private readonly IReadOnlyList<FunctionParameter> _parameters;
    private readonly InlineFunctionItem _implementation;
    private readonly XdmSequenceType? _returnType;

    public DeclaredFunction(QName name, IReadOnlyList<FunctionParameter> parameters, InlineFunctionItem implementation, XdmSequenceType? returnType = null, string? moduleBaseUri = null)
    {
        _name = name;
        _parameters = parameters;
        _implementation = implementation;
        _returnType = returnType;
        _ = moduleBaseUri; // ModuleBaseUri is already propagated into the InlineFunctionItem at construction.
    }

    public override QName Name => _name;
    public override XdmSequenceType ReturnType => _returnType ?? XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        _parameters.Select(p => new FunctionParameterDef
        {
            Name = p.Name,
            Type = p.Type ?? XdmSequenceType.ZeroOrMoreItems
        }).ToList();

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var result = await _implementation.InvokeAsync(arguments, context);
        if (_returnType == null)
            return result;

        // Always check cardinality, even for item()/node() return types
        // XDM arrays (List<object?>) and maps (Dictionary) are single items, not sequences
        var resultItems = result switch
        {
            null => (IReadOnlyList<object?>)Array.Empty<object?>(),
            IDictionary<object, object?> => new object?[] { result },
            List<object?> => new object?[] { result },
            object?[] arr => arr,
            _ => new object?[] { result }
        };

        // Check occurrence (cardinality) for all return types
        if (_returnType.Occurrence == Occurrence.ExactlyOne && resultItems.Count != 1)
            throw new XQueryRuntimeException("XPTY0004",
                $"Result of function {_name.LocalName} does not match declared return type {_returnType}: expected exactly one item, got {resultItems.Count}");
        if (_returnType.Occurrence == Occurrence.OneOrMore && resultItems.Count == 0)
            throw new XQueryRuntimeException("XPTY0004",
                $"Result of function {_name.LocalName} does not match declared return type {_returnType}: expected one or more items, got empty sequence");
        if (_returnType.Occurrence == Occurrence.ZeroOrOne && resultItems.Count > 1)
            throw new XQueryRuntimeException("XPTY0004",
                $"Result of function {_name.LocalName} does not match declared return type {_returnType}: expected at most one item, got {resultItems.Count}");

        // For item() return type, only cardinality check is needed (already done above)
        if (_returnType.ItemType == ItemType.Item)
            return result;

        // For node-typed returns, enforce that result items match the kind test
        if (_returnType.ItemType is ItemType.Element or ItemType.Attribute
            or ItemType.Text or ItemType.Document or ItemType.Comment
            or ItemType.ProcessingInstruction or ItemType.Node)
        {
            foreach (var item in resultItems)
            {
                if (item != null)
                {
                    if (!TypeCastHelper.MatchesItemType(item, _returnType.ItemType))
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Result of function {_name.LocalName} does not match declared return type {_returnType}");
                    // Check element(name) / attribute(name) constraints
                    if (_returnType.ElementName != null && item is XdmElement el
                        && el.LocalName != _returnType.ElementName)
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Result of function {_name.LocalName} does not match declared return type element({_returnType.ElementName}): got element({el.LocalName})");
                    if (_returnType.AttributeName != null && item is XdmAttribute attr
                        && attr.LocalName != _returnType.AttributeName)
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Result of function {_name.LocalName} does not match declared return type attribute({_returnType.AttributeName}): got attribute({attr.LocalName})");
                }
            }
            return result;
        }

        // Only enforce coercion/promotion for atomic targets.
        var enforce = _returnType.ItemType is not (
            ItemType.Map or ItemType.Array or ItemType.Record);
        // For function-typed return, apply function coercion per XPath 3.1 §3.1.5.2:
        // wrap the function with the target type signature, deferring type checks to invocation time.
        if (_returnType.ItemType == ItemType.Function && _returnType.FunctionParameterTypes != null)
        {
            var anyCoerced = false;
            var coercedItems = new object?[resultItems.Count];
            for (int fi = 0; fi < resultItems.Count; fi++)
            {
                if (resultItems[fi] is XQueryFunction retFn
                    && retFn.Arity == _returnType.FunctionParameterTypes.Count
                    && !TypeCastHelper.MatchesFunctionType(retFn,
                        _returnType.FunctionParameterTypes, _returnType.FunctionReturnType))
                {
                    coercedItems[fi] = new InlineFunctionItem.CoercedFunctionItem(retFn,
                        _returnType.FunctionParameterTypes, _returnType.FunctionReturnType);
                    anyCoerced = true;
                }
                else
                {
                    coercedItems[fi] = resultItems[fi];
                }
            }
            if (anyCoerced)
                return coercedItems.Length == 1 ? coercedItems[0] : coercedItems;
            enforce = false;
        }
        else if (_returnType.ItemType == ItemType.Function)
        {
            enforce = false;
        }
        if (!enforce)
            return result;
        var qec = context as QueryExecutionContext;
        var items = resultItems;

        // Apply per-item function-conversion-rules coercion (atomize / cast / promote)
        var coerced = new object?[items.Count];
        var anyCoercion = false;
        var isAtomicTarget = _returnType.ItemType is not (
            ItemType.Node or ItemType.Element or ItemType.Attribute
            or ItemType.Text or ItemType.Document or ItemType.Comment
            or ItemType.ProcessingInstruction or ItemType.Function
            or ItemType.Map or ItemType.Array);
        for (int i = 0; i < items.Count; i++)
        {
            var v = items[i];
            if (v != null && isAtomicTarget)
            {
                if (v is XdmNode node)
                {
                    // Per XDM spec: comment and PI nodes have typed value xs:string,
                    // while text/element nodes have xs:untypedAtomic.
                    // Function conversion rules only cast xs:untypedAtomic to the target type,
                    // so comment/PI atomization must remain xs:string (not XsUntypedAtomic).
                    var isStringTypedNode = node is XdmComment or XdmProcessingInstruction;
                    var atomizedStr = qec != null
                        ? QueryExecutionContext.Atomize(v, qec.NodeProvider)
                        : QueryExecutionContext.Atomize(v, null);
                    v = (atomizedStr is string s && !isStringTypedNode) ? new XsUntypedAtomic(s) : atomizedStr;
                    anyCoercion = true;
                }
                if (v is XsUntypedAtomic ua)
                {
                    try { v = TypeCastHelper.CastValue(ua.Value?.Trim() ?? "", _returnType.ItemType); anyCoercion = true; }
                    catch { /* keep original */ }
                }
                else if (v != null
                    && !TypeCastHelper.MatchesItemType(v, _returnType.ItemType)
                    && IsReturnPromotion(v, _returnType.ItemType))
                {
                    v = PromoteReturn(v, _returnType.ItemType);
                    anyCoercion = true;
                }
            }
            coerced[i] = v;
        }

        if (!TypeCastHelper.MatchesType(coerced, _returnType))
        {
            throw new XQueryRuntimeException("XPTY0004",
                $"Result of function {_name.LocalName} does not match declared return type");
        }

        if (!anyCoercion) return result;
        return coerced.Length switch
        {
            0 => null,
            1 => coerced[0],
            _ => coerced
        };
    }

    private static bool IsReturnPromotion(object value, ItemType target) => target switch
    {
        ItemType.Double => value is int or long or float or decimal or BigInteger,
        ItemType.Float => value is int or long or decimal or BigInteger,
        ItemType.Decimal => value is int or long or BigInteger,
        ItemType.String => value is Xdm.XsAnyUri,
        _ => false
    };

    private static object? PromoteReturn(object value, ItemType target) => target switch
    {
        ItemType.Double => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
        ItemType.Float => Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture),
        ItemType.Decimal => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
        ItemType.String => value.ToString(),
        _ => value
    };
}
