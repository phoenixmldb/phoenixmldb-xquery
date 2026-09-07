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
/// Wrapper for variadic function references that reports the requested arity.
/// E.g., concat#5 should report arity 5, not concat's minimum arity of 2.
/// </summary>
public sealed class VariadicFunctionRefItem : XQueryFunction
{
    private readonly XQueryFunction _inner;
    private readonly int _requestedArity;

    public VariadicFunctionRefItem(XQueryFunction inner, int requestedArity)
    {
        _inner = inner;
        _requestedArity = requestedArity;
    }

    public override QName Name => _inner.Name;
    public override XdmSequenceType ReturnType => _inner.ReturnType;
    public override IReadOnlyList<FunctionParameterDef> Parameters
    {
        get
        {
            // Generate parameter defs for the requested arity
            var baseParms = _inner.Parameters;
            if (_requestedArity <= baseParms.Count) return baseParms;
            var parms = new List<FunctionParameterDef>(baseParms);
            for (int i = baseParms.Count; i < _requestedArity; i++)
                parms.Add(new FunctionParameterDef
                {
                    Name = new QName(NamespaceId.None, $"arg{i + 1}"),
                    Type = XdmSequenceType.ZeroOrMoreItems
                });
            return parms;
        }
    }

    // A named function reference concat#N has fixed arity N — it's no longer variadic.
    public override bool IsVariadic => false;
    public override int MaxArity => _requestedArity;

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        PhoenixmlDb.XQuery.Ast.ExecutionContext context)
        => _inner.InvokeAsync(arguments, context);
}
