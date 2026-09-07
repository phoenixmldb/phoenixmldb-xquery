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
/// Function reference that captures a focus (context item) at creation time, as required
/// by XPath 3.1 §3.1.6 for context-dependent named function references. On invocation the
/// captured focus is pushed before delegating to the inner function.
/// </summary>
public sealed class ContextBoundFunctionRef : XQueryFunction
{
    private readonly XQueryFunction _inner;
    private readonly object? _capturedContextItem;
    private readonly string? _capturedStaticBaseUri;
    private readonly bool _hasCapturedStaticBaseUri;
    private readonly int _capturedPosition;
    private readonly int _capturedSize;

    public ContextBoundFunctionRef(XQueryFunction inner, object? capturedContextItem)
    {
        _inner = inner;
        _capturedContextItem = capturedContextItem;
        _hasCapturedStaticBaseUri = false;
        _capturedPosition = 1;
        _capturedSize = 1;
    }

    public ContextBoundFunctionRef(XQueryFunction inner, object? capturedContextItem, string? capturedStaticBaseUri)
    {
        _inner = inner;
        _capturedContextItem = capturedContextItem;
        _capturedStaticBaseUri = capturedStaticBaseUri;
        _hasCapturedStaticBaseUri = true;
        _capturedPosition = 1;
        _capturedSize = 1;
    }

    public ContextBoundFunctionRef(XQueryFunction inner, object? capturedContextItem, string? capturedStaticBaseUri, int position, int size)
    {
        _inner = inner;
        _capturedContextItem = capturedContextItem;
        _capturedStaticBaseUri = capturedStaticBaseUri;
        _hasCapturedStaticBaseUri = true;
        _capturedPosition = position;
        _capturedSize = size;
    }

    public override QName Name => _inner.Name;
    public override XdmSequenceType ReturnType => _inner.ReturnType;
    public override IReadOnlyList<FunctionParameterDef> Parameters => _inner.Parameters;
    public override bool IsVariadic => _inner.IsVariadic;
    public override int MaxArity => _inner.MaxArity;

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        PhoenixmlDb.XQuery.Ast.ExecutionContext context)
    {
        if (context is QueryExecutionContext qec)
        {
            qec.PushContextItem(_capturedContextItem, _capturedPosition, _capturedSize);
            string? savedBaseUri = qec.StaticBaseUri;
            if (_hasCapturedStaticBaseUri)
                qec.StaticBaseUri = _capturedStaticBaseUri;
            try
            {
                return await _inner.InvokeAsync(arguments, context);
            }
            finally
            {
                if (_hasCapturedStaticBaseUri)
                    qec.StaticBaseUri = savedBaseUri;
                qec.PopContextItem();
            }
        }
        return await _inner.InvokeAsync(arguments, context);
    }
}
