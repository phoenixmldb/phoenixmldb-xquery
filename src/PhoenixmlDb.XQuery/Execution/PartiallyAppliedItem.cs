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
/// A partially applied function item. When called, merges placeholder and fixed arguments.
/// </summary>
public sealed class PartiallyAppliedItem : XQueryFunction
{
    private readonly XQueryFunction _targetFunc;
    private readonly object?[] _fixedValues;
    private readonly bool[] _isPlaceholder;
    private readonly int _placeholderCount;

    public PartiallyAppliedItem(XQueryFunction targetFunc, object?[] fixedValues, bool[] isPlaceholder, int placeholderCount)
    {
        _targetFunc = targetFunc;
        _fixedValues = fixedValues;
        _isPlaceholder = isPlaceholder;
        _placeholderCount = placeholderCount;
    }

    public override QName Name => _targetFunc.Name;
    // XQUF 3.0 §7.10: a partial application of an updating function returns an updating function.
    internal override bool IsUpdating => _targetFunc.IsUpdating;
    public override bool IsAnonymous => true;
    public override XdmSequenceType ReturnType => _targetFunc.ReturnType;
    public override IReadOnlyList<FunctionParameterDef> Parameters
    {
        get
        {
            var result = new List<FunctionParameterDef>();
            var sourceParams = _targetFunc.Parameters;
            for (int i = 0; i < _isPlaceholder.Length; i++)
            {
                if (_isPlaceholder[i])
                {
                    result.Add(i < sourceParams.Count
                        ? sourceParams[i]
                        : new FunctionParameterDef { Name = new QName(default, $"arg{i}"), Type = XdmSequenceType.ZeroOrMoreItems });
                }
            }
            return result;
        }
    }

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Merge fixed values and placeholder arguments
        var mergedArgs = new object?[_fixedValues.Length];
        int placeholderIdx = 0;
        for (int i = 0; i < mergedArgs.Length; i++)
        {
            if (_isPlaceholder[i])
            {
                mergedArgs[i] = placeholderIdx < arguments.Count ? arguments[placeholderIdx] : null;
                placeholderIdx++;
            }
            else
            {
                mergedArgs[i] = _fixedValues[i];
            }
        }
        // If the target was a placeholder for a user-declared function (captured at compile/plan time),
        // resolve to the real DeclaredFunction at invoke time.
        var target = _targetFunc;
        if (target.GetType().Name == "DeclaredFunctionPlaceholder" && context is QueryExecutionContext qec)
            target = qec.Functions.Resolve(target.Name, mergedArgs.Length) ?? target;

        // The function conversion rules (XPath 3.1 §3.1.5.2): for a built-in, or a function under a built-in's
        // name and arity, the conversion of a static call of it (BuiltInArguments); otherwise the item types
        // and cardinalities of the declared parameters.
        if (context is QueryExecutionContext query && BuiltInArguments.For(target, mergedArgs.Length) is { } builtIn)
        {
            builtIn.Convert(target, mergedArgs, query);
        }
        else
        {
            var targetParams = target.Parameters;
            for (int i = 0; i < mergedArgs.Length && i < targetParams.Count; i++)
            {
                if (targetParams[i].Type != null)
                    TypeCastHelper.ValidateDynamicFunctionArg(mergedArgs[i], targetParams[i].Type, target.Name.LocalName, i);
            }
        }

        return await target.InvokeAsync(mergedArgs, context);
    }
}
