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
/// Represents a runtime inline function value (closure).
/// </summary>
public sealed class InlineFunctionItem : XQueryFunction
{
    private readonly IReadOnlyList<FunctionParameter> _parameters;
    private readonly XQueryExpression _body;
    private readonly QueryExecutionContext _capturedContext;
    private readonly Dictionary<QName, object?>? _closureVariables;
    private readonly XdmSequenceType? _declaredReturnType;
    private readonly string? _capturedBaseUri;
    private readonly string? _moduleTargetNamespace;
    /// <summary>
    /// The copy-namespaces mode of the library module that declared this function.
    /// Null for main-module functions or anonymous inline functions.
    /// Per XQuery §4.4, element constructors in an imported library module use the
    /// module's own copy-namespaces mode, not the importing query's mode.
    /// </summary>
    private readonly Analysis.CopyNamespacesMode? _moduleCopyNamespacesMode;
    private ExecutionPlan? _cachedPlan;

    public InlineFunctionItem(
        IReadOnlyList<FunctionParameter> parameters,
        XQueryExpression body,
        QueryExecutionContext context,
        XdmSequenceType? declaredReturnType = null,
        string? moduleBaseUri = null,
        string? moduleTargetNamespace = null,
        Analysis.CopyNamespacesMode? moduleCopyNamespacesMode = null)
    {
        _parameters = parameters;
        _body = body;
        _capturedContext = context;
        _declaredReturnType = declaredReturnType;
        _moduleTargetNamespace = moduleTargetNamespace;
        _moduleCopyNamespacesMode = moduleCopyNamespacesMode;
        // Capture a snapshot of all in-scope variables to support closures.
        // Without this, variables from enclosing scopes (e.g., XSLT function params)
        // would be lost when the closure is invoked after the enclosing scope exits.
        _closureVariables = context.CaptureVariables();
        // Static base URI override: library-module functions carry their module's base-uri;
        // anonymous inline functions capture the current static base uri so closures preserve
        // the static context at creation time (XPath 3.1 §3.1.7).
        _capturedBaseUri = moduleBaseUri ?? context.StaticBaseUri;
    }

    public override QName Name => new(default, "anonymous");
    public override bool IsAnonymous => true;
    public override XdmSequenceType ReturnType => _declaredReturnType ?? XdmSequenceType.ZeroOrMoreItems;
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
        // Per XPath/XQuery 3.1 §3.1.5.1, function invocation evaluates the body in the
        // function's static context (declared in the module that contains the function),
        // not the caller's. The function library, namespace bindings, and other static
        // context elements travel with the closure. When a closure registered in a
        // sub-engine (e.g. via fn:load-xquery-module) is invoked from an outer engine,
        // using the caller's context would fail to resolve transitively-imported
        // functions like f1:foo from f2:bar (Martin Honnen 2026-05-20 load-module repro).
        // For an anonymous inline function created inside the same context as the caller,
        // _capturedContext and context are the same object, so this is a no-op for the
        // common case.
        var execContext = _capturedContext;

        execContext.EnterFunctionCall();
        // Per XPath/XQuery spec §3.1.5.1, the focus inside a function body is initially
        // undefined — accessing ., position(), or last() must raise XPDY0002 unless the
        // function explicitly sets a focus.
        execContext.PushContextItem(QueryExecutionContext.AbsentFocus);
        // Override the dynamic StaticBaseUri with the base URI captured at function-item
        // creation time (the module's declared base-uri, or the enclosing static context).
        var savedBaseUri = execContext.StaticBaseUri;
        bool baseUriOverridden = false;
        if (_capturedBaseUri != null)
        {
            execContext.StaticBaseUri = _capturedBaseUri;
            baseUriOverridden = true;
        }
        // Track the declaring module's target namespace so that fn:format-number can
        // resolve unqualified decimal-format names against the module's own declarations
        // rather than the caller's (XQuery 4.0 §4.18 module isolation).
        var savedModuleNamespace = execContext.CurrentModuleNamespace;
        if (_moduleTargetNamespace != null)
            execContext.CurrentModuleNamespace = _moduleTargetNamespace;
        // Per XQuery §4.4, the copy-namespaces declaration applies to element constructors
        // in the module where it is declared. Library modules that don't declare
        // copy-namespaces use the default (preserve, inherit). We save/restore so the
        // module function's element constructors use the module's own mode, not the
        // calling query's potentially-different mode.
        var savedCopyNsMode = execContext.CopyNamespacesMode;
        if (_moduleCopyNamespacesMode.HasValue)
            execContext.CopyNamespacesMode = _moduleCopyNamespacesMode.Value;
        // Push closure scope with captured variables from enclosing context
        execContext.PushScope();
        if (_closureVariables != null)
        {
            foreach (var (name, value) in _closureVariables)
                execContext.BindVariable(name, value);
        }
        // Push parameter scope on top so params shadow closure variables
        execContext.PushScope();
        try
        {
            for (int i = 0; i < _parameters.Count && i < arguments.Count; i++)
            {
                var arg = arguments[i];
                var paramType = _parameters[i].Type;
                // Function argument coercion per XQuery 3.1 §3.1.5.2:
                // 1. Atomize node arguments
                // 2. Cast xs:untypedAtomic to expected type
                // 3. Numeric promotion (integer→double, etc.)
                // Unwrap single-item sequences (object?[])
                if (arg is object?[] singleArr && singleArr.Length == 1)
                    arg = singleArr[0];
                // Unwrap single-element arrays (List<object?>) ONLY when param expects atomic types,
                // NOT when param is item()/function(*)/array(*)/node-types (arrays are items).
                else if (arg is List<object?> singleList && singleList.Count == 1
                    && paramType?.ItemType is not (null or Ast.ItemType.Item or Ast.ItemType.Array
                        or Ast.ItemType.Function or Ast.ItemType.Map
                        or Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
                        or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
                        or Ast.ItemType.ProcessingInstruction))
                    arg = singleList[0];

                // Handle multi-item sequences: object?[] is always a sequence;
                // List<object?> is an XDM array but treated as a sequence when param expects atomic types
                bool isMultiItemSequence = paramType != null
                    && (arg is object?[] seqArr && seqArr.Length != 1
                        || (arg is List<object?> seqList && seqList.Count != 1
                            && paramType.ItemType is not (Ast.ItemType.Item or Ast.ItemType.Array
                                or Ast.ItemType.Function or Ast.ItemType.Map
                                or Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
                                or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
                                or Ast.ItemType.ProcessingInstruction)));
                if (isMultiItemSequence)
                {
                    var items = arg is object?[] sa ? sa : ((List<object?>)arg!).ToArray();
                    var isAtomicTgt = paramType!.ItemType is not (
                        Ast.ItemType.Item or Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
                        or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
                        or Ast.ItemType.ProcessingInstruction or Ast.ItemType.Function
                        or Ast.ItemType.Map or Ast.ItemType.Array);
                    if (!isAtomicTgt)
                    {
                        // Non-atomic target: pass sequence as-is
                        execContext.BindVariable(_parameters[i].Name, arg);
                        continue;
                    }
                    // Atomic target with sequence occurrence (*,+): coerce each item
                    if (paramType.Occurrence is Ast.Occurrence.ZeroOrMore or Ast.Occurrence.OneOrMore)
                    {
                        var coerced = new object?[items.Length];
                        for (int j = 0; j < items.Length; j++)
                        {
                            var it = items[j];
                            if (it is XdmNode) it = QueryExecutionContext.AtomizeTyped(it);
                            // Function conversion rules cast xs:untypedAtomic to a *specific*
                            // required atomic type. When the required type is the generic
                            // xs:anyAtomicType (or xs:untypedAtomic itself), no cast occurs —
                            // the value keeps its xs:untypedAtomic type so downstream
                            // operations (e.g. fn:sum casting untypedAtomic to xs:double) see
                            // the right type rather than a bare xs:string.
                            if (it is XsUntypedAtomic ua
                                && paramType.ItemType is not (Ast.ItemType.AnyAtomicType or Ast.ItemType.UntypedAtomic))
                            {
                                try { it = TypeCastHelper.CastValue(ua.Value, paramType.ItemType); }
                                catch { /* keep original */ }
                            }
                            // Numeric promotion
                            if (it != null && !TypeCastHelper.MatchesItemType(it, paramType.ItemType)
                                && IsInlineParamPromotion(it, paramType.ItemType))
                            {
                                it = TypeCastHelper.PromoteNumeric(it, paramType.ItemType);
                            }
                            coerced[j] = it;
                        }
                        execContext.BindVariable(_parameters[i].Name, coerced);
                        continue;
                    }
                    // Sequence but single-item parameter — type error
                    throw new XQueryRuntimeException("XPTY0004",
                        $"Inline function parameter ${_parameters[i].Name.LocalName} expects " +
                        $"{paramType} but got a sequence of {items.Length} items");
                }
                // Cardinality check: empty sequence (null) for exactly-one / one-or-more parameter
                if (arg == null && paramType != null
                    && paramType.Occurrence is Ast.Occurrence.ExactlyOne or Ast.Occurrence.OneOrMore)
                {
                    throw new XQueryRuntimeException("XPTY0004",
                        $"Parameter ${_parameters[i].Name.LocalName} expects {paramType} " +
                        $"but got an empty sequence");
                }
                if (paramType != null && arg != null
                    && paramType.ItemType != Ast.ItemType.Item
                    && paramType.ItemType != Ast.ItemType.AnyAtomicType)
                {
                    var coercedArg = arg;
                    // Only atomize for atomic parameter types (not node types like element(), document-node())
                    var isAtomicParamType = paramType.ItemType is not (
                        Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
                        or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
                        or Ast.ItemType.ProcessingInstruction
                        or Ast.ItemType.Function or Ast.ItemType.Map or Ast.ItemType.Array);
                    if (coercedArg is XdmNode && isAtomicParamType)
                        coercedArg = QueryExecutionContext.AtomizeTyped(coercedArg);
                    // XPath/XQuery 3.0+ §3.1.5.1 function coercion: implicit cast from
                    // xs:untypedAtomic to a namespace-sensitive type (xs:QName, xs:NOTATION)
                    // during function-argument coercion is NOT allowed — raise XPTY0117.
                    // (Explicit "cast as xs:QName" inside a cast expression IS allowed per bug 16089.)
                    if (coercedArg is XsUntypedAtomic && paramType.ItemType == Ast.ItemType.QName)
                    {
                        throw new XQueryRuntimeException("XPTY0117",
                            $"Implicit cast from xs:untypedAtomic to xs:QName is not allowed " +
                            $"during function coercion (parameter ${_parameters[i].Name.LocalName})");
                    }
                    // Cast untypedAtomic to expected type
                    if (coercedArg is XsUntypedAtomic ua)
                    {
                        try { coercedArg = TypeCastHelper.CastValue(ua.Value, paramType.ItemType); }
                        catch { /* keep original if cast fails */ }
                    }
                    // Numeric/URI promotion: convert value to target type
                    if (coercedArg != null
                        && coercedArg is not XsUntypedAtomic
                        && !TypeCastHelper.MatchesItemType(coercedArg, paramType.ItemType)
                        && IsInlineParamPromotion(coercedArg, paramType.ItemType))
                    {
                        coercedArg = TypeCastHelper.PromoteNumeric(coercedArg, paramType.ItemType);
                    }
                    // Type check after coercion and promotion
                    if (coercedArg != null
                        && coercedArg is not XsUntypedAtomic
                        && !TypeCastHelper.MatchesItemType(coercedArg, paramType.ItemType))
                    {
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Parameter ${_parameters[i].Name.LocalName} expects " +
                            $"{paramType} but got {XdmShape.TypeNameOf(coercedArg)}");
                    }
                    // Parameterized map/array type checking: check key/value/member types
                    if (coercedArg != null && paramType.ItemType is Ast.ItemType.Function
                        && paramType.FunctionParameterTypes != null
                        && coercedArg is XQueryFunction fnArg)
                    {
                        // Function coercion per XPath 3.1 §3.1.5.2:
                        // If the function item doesn't exactly match the target typed function type,
                        // wrap it in a coerced function that presents the target type signature.
                        // Actual argument/return type checking is deferred to invocation time.
                        if (fnArg.Arity == paramType.FunctionParameterTypes.Count
                            && !TypeCastHelper.MatchesFunctionType(fnArg,
                                paramType.FunctionParameterTypes, paramType.FunctionReturnType))
                        {
                            coercedArg = new CoercedFunctionItem(fnArg,
                                paramType.FunctionParameterTypes, paramType.FunctionReturnType);
                        }
                    }
                    else if (coercedArg != null && paramType.ItemType is not Ast.ItemType.Function)
                    {
                        var items = TypeCastHelper.NormalizeToList(coercedArg);
                        if (!TypeCastHelper.MatchesType(items, paramType))
                        {
                            throw new XQueryRuntimeException("XPTY0004",
                                $"Parameter ${_parameters[i].Name.LocalName} expects " +
                                $"{paramType} but got {XdmShape.TypeNameOf(coercedArg)}");
                        }
                    }
                    arg = coercedArg;
                }
                execContext.BindVariable(_parameters[i].Name, arg);
            }

            // Cache the execution plan - optimize only once per function instance
            _cachedPlan ??= new Optimizer.QueryOptimizer()
                .Optimize(_body, new Optimizer.OptimizationContext { Container = default });

            var results = new List<object?>();
            await foreach (var item in _cachedPlan.Root.ExecuteAsync(execContext))
                results.Add(item);

            var result = results.Count switch
            {
                0 => (object?)null,
                1 => results[0],
                _ => results.ToArray()
            };

            // Return type checking per XQuery 3.1 §3.1.5.1:
            // If a return type is declared, coerce/check the result.
            if (_declaredReturnType != null
                && _declaredReturnType.ItemType != Ast.ItemType.Item
                && _declaredReturnType.ItemType != Ast.ItemType.AnyAtomicType)
            {
                result = CoerceReturnValue(result, _declaredReturnType);
            }

            return result;
        }
        finally
        {
            execContext.PopScope(); // parameter scope
            execContext.PopScope(); // closure scope
            execContext.PopContextItem(); // absent focus
            if (baseUriOverridden)
                execContext.StaticBaseUri = savedBaseUri;
            execContext.CurrentModuleNamespace = savedModuleNamespace;
            if (_moduleCopyNamespacesMode.HasValue)
                execContext.CopyNamespacesMode = savedCopyNsMode;
            execContext.ExitFunctionCall();
        }
    }

    /// <summary>
    /// Wraps a function item with a coerced type signature per XPath 3.1 §3.1.5.2.
    /// The wrapper preserves the original function's name and identity but presents
    /// the target type signature. Actual argument/return type checking is deferred to
    /// invocation time.
    /// </summary>
    internal sealed class CoercedFunctionItem : XQueryFunction
    {
        private readonly XQueryFunction _inner;
        private readonly IReadOnlyList<FunctionParameterDef> _targetParams;
        private readonly XdmSequenceType _targetReturnType;

        public CoercedFunctionItem(
            XQueryFunction inner,
            IReadOnlyList<XdmSequenceType> targetParamTypes,
            XdmSequenceType? targetReturnType)
        {
            _inner = inner;
            _targetReturnType = targetReturnType ?? XdmSequenceType.ZeroOrMoreItems;
            // Build parameter defs with the target types but preserve parameter names from inner
            var paramDefs = new FunctionParameterDef[targetParamTypes.Count];
            for (int i = 0; i < targetParamTypes.Count; i++)
            {
                var name = i < inner.Parameters.Count
                    ? inner.Parameters[i].Name
                    : new QName(default, $"arg{i}");
                paramDefs[i] = new FunctionParameterDef
                {
                    Name = name,
                    Type = targetParamTypes[i]
                };
            }
            _targetParams = paramDefs;
        }

        public override QName Name => _inner.Name;
        public override bool IsAnonymous => _inner.IsAnonymous;
        public override XdmSequenceType ReturnType => _targetReturnType;
        public override IReadOnlyList<FunctionParameterDef> Parameters => _targetParams;

        public override async ValueTask<object?> InvokeAsync(
            IReadOnlyList<object?> arguments,
            Ast.ExecutionContext context)
        {
            // Coerce arguments to target types before delegating to inner function.
            // This ensures the inner function sees values in the target type's domain,
            // causing XPTY0004 when inner param type is narrower than target type.
            var coerced = new object?[arguments.Count];
            for (int i = 0; i < arguments.Count && i < _targetParams.Count; i++)
            {
                var arg = arguments[i];
                var targetType = _targetParams[i].Type;
                if (arg != null && targetType != null
                    && targetType.ItemType is not (Ast.ItemType.Item or Ast.ItemType.AnyAtomicType))
                {
                    // Numeric promotion to target type
                    if (!TypeCastHelper.MatchesItemType(arg, targetType.ItemType)
                        && IsInlineParamPromotion(arg, targetType.ItemType))
                    {
                        arg = TypeCastHelper.PromoteNumeric(arg, targetType.ItemType);
                    }
                }
                coerced[i] = arg;
            }
            for (int i = _targetParams.Count; i < arguments.Count; i++)
                coerced[i] = arguments[i];
            return await _inner.InvokeAsync(coerced, context);
        }
    }

    /// <summary>
    /// Coerce a function return value to the declared return type.
    /// Applies numeric promotion and type checking per XQuery 3.1 §3.1.5.
    /// </summary>
    private static object? CoerceReturnValue(object? value, XdmSequenceType declaredType)
    {
        if (value == null)
        {
            if (declaredType.Occurrence is Ast.Occurrence.ExactlyOne or Ast.Occurrence.OneOrMore)
                throw new XQueryRuntimeException("XPTY0004",
                    $"Inline function declared return type {declaredType} but got empty sequence");
            return null;
        }

        // For sequence results, coerce each item
        if (value is object?[] arr)
        {
            var coerced = new object?[arr.Length];
            for (int i = 0; i < arr.Length; i++)
                coerced[i] = CoerceSingleReturnItem(arr[i], declaredType.ItemType);
            return coerced;
        }

        return CoerceSingleReturnItem(value, declaredType.ItemType);
    }

    private static object? CoerceSingleReturnItem(object? item, Ast.ItemType targetType)
    {
        if (item == null) return null;

        // Already matches target type — no coercion needed
        if (TypeCastHelper.MatchesItemType(item, targetType))
            return item;

        // Try numeric promotion (integer→double, integer→float, etc.)
        if (IsInlineParamPromotion(item, targetType))
            return TypeCastHelper.PromoteNumeric(item, targetType);

        // XQuery 3.1 §3.1.5.2: xs:untypedAtomic → cast to expected atomic type
        if (item is Xdm.XsUntypedAtomic ua)
        {
            try
            {
                return TypeCastHelper.CastValue(ua.Value, targetType);
            }
            catch
            {
                throw new XQueryRuntimeException("XPTY0004",
                    $"Cannot cast xs:untypedAtomic value '{ua.Value}' to {targetType}");
            }
        }

        // Type mismatch — raise XPTY0004
        throw new XQueryRuntimeException("XPTY0004",
            $"Inline function declared return type {targetType} but got {item.GetType().Name} value '{item}'");
    }

    private static bool IsInlineParamPromotion(object value, Ast.ItemType target)
    {
        // XQuery 3.1 §3.1.5.3: numeric type promotion + URI-to-string promotion
        return target switch
        {
            Ast.ItemType.Double => value is int or long or float or decimal or System.Numerics.BigInteger,
            Ast.ItemType.Float => value is int or long or decimal or System.Numerics.BigInteger,
            Ast.ItemType.Decimal => value is int or long or System.Numerics.BigInteger,
            Ast.ItemType.String => value is Xdm.XsAnyUri,
            _ => false
        };
    }
}
