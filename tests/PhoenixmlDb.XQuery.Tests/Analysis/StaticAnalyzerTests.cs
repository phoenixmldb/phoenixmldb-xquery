using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Analysis;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Analysis;

/// <summary>
/// Tests for static analysis components.
/// </summary>
public class StaticAnalyzerTests
{
    #region StaticAnalyzer Tests

    [Fact]
    public void StaticAnalyzer_AnalyzeSimpleLiteral_NoErrors()
    {
        var analyzer = new StaticAnalyzer();
        var expr = new IntegerLiteral { Value = 42 };

        var result = analyzer.Analyze(expr);

        result.HasErrors.Should().BeFalse();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void StaticAnalyzer_AnalyzeEmptySequence_NoErrors()
    {
        var analyzer = new StaticAnalyzer();
        var expr = EmptySequence.Instance;

        var result = analyzer.Analyze(expr);

        result.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void StaticAnalyzer_WithDefaultContext_Works()
    {
        var analyzer = new StaticAnalyzer();
        var expr = new StringLiteral { Value = "test" };

        var result = analyzer.Analyze(expr);

        result.Expression.Should().NotBeNull();
    }

    [Fact]
    public void StaticAnalyzer_WithCustomContext_Works()
    {
        var context = new StaticContext
        {
            BaseUri = "http://example.com"
        };
        var analyzer = new StaticAnalyzer(context);
        var expr = new StringLiteral { Value = "test" };

        var result = analyzer.Analyze(expr);

        result.HasErrors.Should().BeFalse();
    }

    #endregion

    #region NamespaceResolver Tests

    [Fact]
    public void NamespaceResolver_ResolvesXsPrefix()
    {
        var namespaces = new NamespaceContext();
        var errors = new List<AnalysisError>();
        var resolver = new NamespaceResolver(namespaces);

        var funcCall = new FunctionCallExpression
        {
            Name = new QName(NamespaceId.None, "test", "xs"),
            Arguments = []
        };

        resolver.Resolve(funcCall, errors);

        // xs prefix is pre-registered
        errors.Where(e => e.Code == XQueryErrorCodes.XPST0081).Should().BeEmpty();
    }

    [Fact]
    public void NamespaceResolver_ResolvesCustomPrefix()
    {
        var namespaces = new NamespaceContext();
        namespaces.RegisterNamespace("myns", "http://example.com/myns");
        var errors = new List<AnalysisError>();
        var resolver = new NamespaceResolver(namespaces);

        var funcCall = new FunctionCallExpression
        {
            Name = new QName(NamespaceId.None, "test", "myns"),
            Arguments = []
        };

        resolver.Resolve(funcCall, errors);

        errors.Where(e => e.Code == XQueryErrorCodes.XPST0081).Should().BeEmpty();
    }

    [Fact]
    public void NamespaceResolver_UnboundPrefix_AddsError()
    {
        var namespaces = new NamespaceContext();
        var errors = new List<AnalysisError>();
        var resolver = new NamespaceResolver(namespaces);

        var funcCall = new FunctionCallExpression
        {
            Name = new QName(NamespaceId.None, "test", "unknown"),
            Arguments = []
        };

        resolver.Resolve(funcCall, errors);

        errors.Should().Contain(e => e.Code == XQueryErrorCodes.XPST0081);
    }

    [Fact]
    public void NamespaceResolver_VariableReference_UnboundPrefix_AddsError()
    {
        var namespaces = new NamespaceContext();
        var errors = new List<AnalysisError>();
        var resolver = new NamespaceResolver(namespaces);

        var varRef = new VariableReference
        {
            Name = new QName(NamespaceId.None, "var", "unknown")
        };

        resolver.Resolve(varRef, errors);

        errors.Should().Contain(e => e.Code == XQueryErrorCodes.XPST0081);
    }

    [Fact]
    public void NamespaceResolver_StepExpression_ResolvesNameTest()
    {
        var namespaces = new NamespaceContext();
        namespaces.RegisterNamespace("ns", "http://example.com/ns");
        var errors = new List<AnalysisError>();
        var resolver = new NamespaceResolver(namespaces);

        var nameTest = new NameTest
        {
            LocalName = "element",
            Prefix = "ns"
        };
        var step = new StepExpression
        {
            Axis = Axis.Child,
            NodeTest = nameTest,
            Predicates = []
        };

        resolver.Resolve(step, errors);

        errors.Where(e => e.Code == XQueryErrorCodes.XPST0081).Should().BeEmpty();
        nameTest.ResolvedNamespace.Should().NotBeNull();
    }

    [Fact]
    public void NamespaceResolver_StepExpression_UnboundPrefix_AddsError()
    {
        var namespaces = new NamespaceContext();
        var errors = new List<AnalysisError>();
        var resolver = new NamespaceResolver(namespaces);

        var nameTest = new NameTest
        {
            LocalName = "element",
            Prefix = "unknown"
        };
        var step = new StepExpression
        {
            Axis = Axis.Child,
            NodeTest = nameTest,
            Predicates = []
        };

        resolver.Resolve(step, errors);

        errors.Should().Contain(e => e.Code == XQueryErrorCodes.XPST0081);
    }

    #endregion

    #region VariableBinder Tests

    [Fact]
    public void VariableBinder_UndefinedVariable_AddsError()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var binder = new VariableBinder(context);

        var varRef = new VariableReference
        {
            Name = new QName(NamespaceId.None, "undefined")
        };

        binder.Bind(varRef, errors);

        errors.Should().Contain(e => e.Code == XQueryErrorCodes.XPST0008);
    }

    [Fact]
    public void VariableBinder_FlworBinding_BoundsVariable()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var binder = new VariableBinder(context);

        var forClause = new ForClause
        {
            Bindings =
            [
                new ForBinding
                {
                    Variable = new QName(NamespaceId.None, "x"),
                    Expression = new IntegerLiteral { Value = 1 }
                }
            ]
        };

        var varRef = new VariableReference
        {
            Name = new QName(NamespaceId.None, "x")
        };

        var flwor = new FlworExpression
        {
            Clauses = [forClause],
            ReturnExpression = varRef
        };

        binder.Bind(flwor, errors);

        errors.Should().BeEmpty();
        varRef.ResolvedBinding.Should().NotBeNull();
    }

    [Fact]
    public void VariableBinder_LetClause_BindsVariable()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var binder = new VariableBinder(context);

        var letClause = new LetClause
        {
            Bindings =
            [
                new LetBinding
                {
                    Variable = new QName(NamespaceId.None, "y"),
                    Expression = new StringLiteral { Value = "test" }
                }
            ]
        };

        var varRef = new VariableReference
        {
            Name = new QName(NamespaceId.None, "y")
        };

        var flwor = new FlworExpression
        {
            Clauses = [letClause],
            ReturnExpression = varRef
        };

        binder.Bind(flwor, errors);

        errors.Should().BeEmpty();
        varRef.ResolvedBinding.Should().NotBeNull();
        varRef.ResolvedBinding!.Scope.Should().Be(VariableScope.Let);
    }

    [Fact]
    public void VariableBinder_PositionalVariable_BindsVariable()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var binder = new VariableBinder(context);

        var forClause = new ForClause
        {
            Bindings =
            [
                new ForBinding
                {
                    Variable = new QName(NamespaceId.None, "x"),
                    PositionalVariable = new QName(NamespaceId.None, "i"),
                    Expression = new IntegerLiteral { Value = 1 }
                }
            ]
        };

        var posRef = new VariableReference
        {
            Name = new QName(NamespaceId.None, "i")
        };

        var flwor = new FlworExpression
        {
            Clauses = [forClause],
            ReturnExpression = posRef
        };

        binder.Bind(flwor, errors);

        errors.Should().BeEmpty();
        posRef.ResolvedBinding.Should().NotBeNull();
        posRef.ResolvedBinding!.Scope.Should().Be(VariableScope.Positional);
    }

    [Fact]
    public void VariableBinder_ScopedVariable_NotVisibleOutside()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var binder = new VariableBinder(context);

        // Variable bound in inner FLWOR should not be visible in outer scope
        var innerFlwor = new FlworExpression
        {
            Clauses =
            [
                new ForClause
                {
                    Bindings =
                    [
                        new ForBinding
                        {
                            Variable = new QName(NamespaceId.None, "inner"),
                            Expression = new IntegerLiteral { Value = 1 }
                        }
                    ]
                }
            ],
            ReturnExpression = new VariableReference { Name = new QName(NamespaceId.None, "inner") }
        };

        // This reference should fail because inner is out of scope
        var outerRef = new VariableReference
        {
            Name = new QName(NamespaceId.None, "inner")
        };

        var sequence = new SequenceExpression
        {
            Items = [innerFlwor, outerRef]
        };

        binder.Bind(sequence, errors);

        errors.Should().Contain(e => e.Code == XQueryErrorCodes.XPST0008);
    }

    #endregion

    #region FunctionResolver Tests

    [Fact]
    public void FunctionResolver_KnownFunction_Resolves()
    {
        var library = FunctionLibrary.Standard;
        var errors = new List<AnalysisError>();
        var resolver = new FunctionResolver(library);

        var funcCall = new FunctionCallExpression
        {
            Name = new QName(FunctionNamespaces.Fn, "string-length"),
            Arguments = [new StringLiteral { Value = "test" }]
        };

        resolver.Resolve(funcCall, errors);

        errors.Should().BeEmpty();
        funcCall.ResolvedFunction.Should().NotBeNull();
    }

    [Fact]
    public void FunctionResolver_UnknownFunction_AddsError()
    {
        var library = FunctionLibrary.Standard;
        var errors = new List<AnalysisError>();
        var resolver = new FunctionResolver(library);

        var funcCall = new FunctionCallExpression
        {
            Name = new QName(FunctionNamespaces.Fn, "nonexistent"),
            Arguments = []
        };

        resolver.Resolve(funcCall, errors);

        errors.Should().Contain(e => e.Code == XQueryErrorCodes.XPST0017);
    }

    [Fact]
    public void FunctionResolver_WrongArity_AddsError()
    {
        var library = FunctionLibrary.Standard;
        var errors = new List<AnalysisError>();
        var resolver = new FunctionResolver(library);

        // string-length takes 0 or 1 argument, not 3
        var funcCall = new FunctionCallExpression
        {
            Name = new QName(FunctionNamespaces.Fn, "string-length"),
            Arguments =
            [
                new StringLiteral { Value = "a" },
                new StringLiteral { Value = "b" },
                new StringLiteral { Value = "c" }
            ]
        };

        resolver.Resolve(funcCall, errors);

        errors.Should().Contain(e => e.Code == XQueryErrorCodes.XPST0017);
    }

    [Fact]
    public void FunctionResolver_NamedFunctionRef_Resolves()
    {
        var library = FunctionLibrary.Standard;
        var errors = new List<AnalysisError>();
        var resolver = new FunctionResolver(library);

        var funcRef = new NamedFunctionRef
        {
            Name = new QName(FunctionNamespaces.Fn, "concat"),
            Arity = 2
        };

        resolver.Resolve(funcRef, errors);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void FunctionResolver_NamedFunctionRef_UnknownFunction_AddsError()
    {
        var library = FunctionLibrary.Standard;
        var errors = new List<AnalysisError>();
        var resolver = new FunctionResolver(library);

        var funcRef = new NamedFunctionRef
        {
            Name = new QName(FunctionNamespaces.Fn, "nonexistent"),
            Arity = 1
        };

        resolver.Resolve(funcRef, errors);

        errors.Should().Contain(e => e.Code == XQueryErrorCodes.XPST0017);
    }

    #endregion

    #region TypeInferrer Tests

    [Fact]
    public void TypeInferrer_IntegerLiteral_InfersInteger()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var inferrer = new TypeInferrer(context);

        var expr = new IntegerLiteral { Value = 42 };

        inferrer.Infer(expr, errors);

        expr.StaticType.Should().NotBeNull();
        expr.StaticType!.ItemType.Should().Be(ItemType.Integer);
        expr.StaticType.Occurrence.Should().Be(Occurrence.ExactlyOne);
    }

    [Fact]
    public void TypeInferrer_DecimalLiteral_InfersDecimal()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var inferrer = new TypeInferrer(context);

        var expr = new DecimalLiteral { Value = 3.14m };

        inferrer.Infer(expr, errors);

        expr.StaticType.Should().NotBeNull();
        expr.StaticType!.ItemType.Should().Be(ItemType.Decimal);
    }

    [Fact]
    public void TypeInferrer_DoubleLiteral_InfersDouble()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var inferrer = new TypeInferrer(context);

        var expr = new DoubleLiteral { Value = 1.5e10 };

        inferrer.Infer(expr, errors);

        expr.StaticType.Should().NotBeNull();
        expr.StaticType!.ItemType.Should().Be(ItemType.Double);
    }

    [Fact]
    public void TypeInferrer_StringLiteral_InfersString()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var inferrer = new TypeInferrer(context);

        var expr = new StringLiteral { Value = "test" };

        inferrer.Infer(expr, errors);

        expr.StaticType.Should().NotBeNull();
        expr.StaticType!.ItemType.Should().Be(ItemType.String);
    }

    [Fact]
    public void TypeInferrer_BooleanLiteral_InfersBoolean()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var inferrer = new TypeInferrer(context);

        var expr = BooleanLiteral.True;

        inferrer.Infer(expr, errors);

        expr.StaticType.Should().NotBeNull();
        expr.StaticType!.ItemType.Should().Be(ItemType.Boolean);
    }

    [Fact]
    public void TypeInferrer_EmptySequence_InfersEmpty()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var inferrer = new TypeInferrer(context);

        var expr = EmptySequence.Instance;

        inferrer.Infer(expr, errors);

        expr.StaticType.Should().Be(XdmSequenceType.Empty);
    }

    [Fact]
    public void TypeInferrer_BinaryArithmetic_InfersNumeric()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var inferrer = new TypeInferrer(context);

        var expr = new BinaryExpression
        {
            Left = new IntegerLiteral { Value = 1 },
            Operator = BinaryOperator.Add,
            Right = new IntegerLiteral { Value = 2 }
        };

        inferrer.Infer(expr, errors);

        expr.StaticType.Should().NotBeNull();
        expr.StaticType!.ItemType.Should().Be(ItemType.Integer);
    }

    [Fact]
    public void TypeInferrer_BinaryComparison_InfersBoolean()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var inferrer = new TypeInferrer(context);

        var expr = new BinaryExpression
        {
            Left = new IntegerLiteral { Value = 1 },
            Operator = BinaryOperator.Equal,
            Right = new IntegerLiteral { Value = 2 }
        };

        inferrer.Infer(expr, errors);

        expr.StaticType.Should().Be(XdmSequenceType.Boolean);
    }

    [Fact]
    public void TypeInferrer_RangeExpression_InfersIntegerSequence()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var inferrer = new TypeInferrer(context);

        var expr = new RangeExpression
        {
            Start = new IntegerLiteral { Value = 1 },
            End = new IntegerLiteral { Value = 10 }
        };

        inferrer.Infer(expr, errors);

        expr.StaticType.Should().NotBeNull();
        expr.StaticType!.ItemType.Should().Be(ItemType.Integer);
        expr.StaticType.Occurrence.Should().Be(Occurrence.ZeroOrMore);
    }

    [Fact]
    public void TypeInferrer_InstanceOf_InfersBoolean()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var inferrer = new TypeInferrer(context);

        var expr = new InstanceOfExpression
        {
            Expression = new IntegerLiteral { Value = 1 },
            TargetType = XdmSequenceType.Integer
        };

        inferrer.Infer(expr, errors);

        expr.StaticType.Should().Be(XdmSequenceType.Boolean);
    }

    [Fact]
    public void TypeInferrer_CastExpression_InfersTargetType()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var inferrer = new TypeInferrer(context);

        var expr = new CastExpression
        {
            Expression = new StringLiteral { Value = "42" },
            TargetType = XdmSequenceType.Integer
        };

        inferrer.Infer(expr, errors);

        expr.StaticType.Should().Be(XdmSequenceType.Integer);
    }

    [Fact]
    public void TypeInferrer_FunctionCall_InfersFunctionReturnType()
    {
        var context = new StaticContext();
        var errors = new List<AnalysisError>();
        var inferrer = new TypeInferrer(context);

        var funcCall = new FunctionCallExpression
        {
            Name = new QName(FunctionNamespaces.Fn, "string-length"),
            Arguments = [new StringLiteral { Value = "test" }],
            ResolvedFunction = new StringLengthFunction()
        };

        inferrer.Infer(funcCall, errors);

        funcCall.StaticType.Should().Be(XdmSequenceType.Integer);
    }

    #endregion

    #region NamespaceContext Tests

    [Fact]
    public void NamespaceContext_DefaultPrefixes_AreRegistered()
    {
        var context = new NamespaceContext();

        context.ResolvePrefix("xml").Should().Be(WellKnownNamespaces.XmlUri);
        context.ResolvePrefix("xs").Should().Be(WellKnownNamespaces.XsUri);
        context.ResolvePrefix("xsi").Should().Be(WellKnownNamespaces.XsiUri);
        context.ResolvePrefix("fn").Should().Be(WellKnownNamespaces.FnUri);
        context.ResolvePrefix("local").Should().Be(WellKnownNamespaces.LocalUri);
        context.ResolvePrefix("map").Should().Be(WellKnownNamespaces.MapUri);
        context.ResolvePrefix("array").Should().Be(WellKnownNamespaces.ArrayUri);
        context.ResolvePrefix("math").Should().Be(WellKnownNamespaces.MathUri);
    }

    [Fact]
    public void NamespaceContext_RegisterNamespace_AddsPrefix()
    {
        var context = new NamespaceContext();

        context.RegisterNamespace("test", "http://example.com/test");

        context.ResolvePrefix("test").Should().Be("http://example.com/test");
    }

    [Fact]
    public void NamespaceContext_UnknownPrefix_ReturnsNull()
    {
        var context = new NamespaceContext();

        context.ResolvePrefix("unknown").Should().BeNull();
    }

    [Fact]
    public void NamespaceContext_GetOrCreateId_ReturnsSameIdForSameUri()
    {
        var context = new NamespaceContext();
        var uri = "http://example.com/test";

        var id1 = context.GetOrCreateId(uri);
        var id2 = context.GetOrCreateId(uri);

        id1.Should().Be(id2);
    }

    [Fact]
    public void NamespaceContext_GetOrCreateId_ReturnsDifferentIdForDifferentUri()
    {
        var context = new NamespaceContext();

        var id1 = context.GetOrCreateId("http://example.com/ns1");
        var id2 = context.GetOrCreateId("http://example.com/ns2");

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void NamespaceContext_Prefixes_ReturnsAllRegisteredPrefixes()
    {
        var context = new NamespaceContext();
        context.RegisterNamespace("custom1", "http://example.com/1");
        context.RegisterNamespace("custom2", "http://example.com/2");

        context.Prefixes.Should().Contain("xml");
        context.Prefixes.Should().Contain("xs");
        context.Prefixes.Should().Contain("fn");
        context.Prefixes.Should().Contain("custom1");
        context.Prefixes.Should().Contain("custom2");
    }

    #endregion

    #region StaticContext Tests

    [Fact]
    public void StaticContext_Default_HasCorrectDefaults()
    {
        var context = StaticContext.Default;

        context.DefaultFunctionNamespace.Should().Be(WellKnownNamespaces.FnUri);
        context.ConstructionMode.Should().Be(ConstructionMode.Preserve);
        context.OrderingMode.Should().Be(OrderingMode.Ordered);
        context.DefaultEmptyOrder.Should().Be(EmptyOrder.Least);
        context.BoundarySpace.Should().Be(BoundarySpace.Strip);
    }

    [Fact]
    public void StaticContext_Namespaces_CanBeCustomized()
    {
        var namespaces = new NamespaceContext();
        namespaces.RegisterNamespace("custom", "http://example.com/custom");

        var context = new StaticContext
        {
            Namespaces = namespaces
        };

        context.Namespaces.ResolvePrefix("custom").Should().Be("http://example.com/custom");
    }

    [Fact]
    public void StaticContext_Functions_CanBeCustomized()
    {
        var customLib = new FunctionLibrary();
        customLib.Register(new StringLengthFunction());

        var context = new StaticContext
        {
            Functions = customLib
        };

        context.Functions.Should().BeSameAs(customLib);
    }

    #endregion

    #region AnalysisResult Tests

    [Fact]
    public void AnalysisResult_WithErrors_HasErrorsIsTrue()
    {
        var errors = new List<AnalysisError>
        {
            new(XQueryErrorCodes.XPST0008, "Test error", null)
        };

        var result = new AnalysisResult(new IntegerLiteral { Value = 1 }, errors);

        result.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void AnalysisResult_WithoutErrors_HasErrorsIsFalse()
    {
        var result = new AnalysisResult(new IntegerLiteral { Value = 1 }, []);

        result.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void AnalysisResult_WithWarnings_HasWarningsIsTrue()
    {
        var errors = new List<AnalysisError>
        {
            new("WARN001", "Test warning", null, AnalysisErrorSeverity.Warning)
        };

        var result = new AnalysisResult(new IntegerLiteral { Value = 1 }, errors);

        result.HasWarnings.Should().BeTrue();
        result.HasErrors.Should().BeFalse();
    }

    #endregion

    #region XQueryErrorCodes Tests

    [Fact]
    public void XQueryErrorCodes_XPST0008_IsUndefinedVariable()
    {
        XQueryErrorCodes.XPST0008.Should().Be("XPST0008");
    }

    [Fact]
    public void XQueryErrorCodes_XPST0017_IsUndefinedFunction()
    {
        XQueryErrorCodes.XPST0017.Should().Be("XPST0017");
    }

    [Fact]
    public void XQueryErrorCodes_XPST0081_IsUnboundPrefix()
    {
        XQueryErrorCodes.XPST0081.Should().Be("XPST0081");
    }

    [Fact]
    public void XQueryErrorCodes_XPTY0004_IsTypeMismatch()
    {
        XQueryErrorCodes.XPTY0004.Should().Be("XPTY0004");
    }

    #endregion
}
