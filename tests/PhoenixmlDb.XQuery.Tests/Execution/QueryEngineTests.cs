using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Analysis;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Tests for query engine and execution.
/// </summary>
public class QueryEngineTests
{
    #region QueryEngine Construction Tests

    [Fact]
    public void QueryEngine_DefaultConstructor_Works()
    {
        var engine = new QueryEngine();

        engine.Should().NotBeNull();
    }

    [Fact]
    public void QueryEngine_WithCustomFunctionLibrary_Works()
    {
        var customLib = FunctionLibrary.Standard;
        var engine = new QueryEngine(functions: customLib);

        engine.Should().NotBeNull();
    }

    [Fact]
    public void QueryEngine_WithNullFunctionLibrary_UsesStandard()
    {
        var engine = new QueryEngine(functions: null);

        // Should not throw
        engine.Should().NotBeNull();
    }

    #endregion

    #region Compile Tests

    [Fact]
    public void Compile_SimpleLiteral_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new IntegerLiteral { Value = 42 };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.ExecutionPlan.Should().NotBeNull();
    }

    [Fact]
    public void Compile_StringLiteral_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new StringLiteral { Value = "hello" };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_BooleanLiteral_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = BooleanLiteral.True;

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_EmptySequence_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = EmptySequence.Instance;

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_BinaryExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new BinaryExpression
        {
            Left = new IntegerLiteral { Value = 1 },
            Operator = BinaryOperator.Add,
            Right = new IntegerLiteral { Value = 2 }
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_SequenceExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new SequenceExpression
        {
            Items =
            [
                new IntegerLiteral { Value = 1 },
                new IntegerLiteral { Value = 2 },
                new IntegerLiteral { Value = 3 }
            ]
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_RangeExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new RangeExpression
        {
            Start = new IntegerLiteral { Value = 1 },
            End = new IntegerLiteral { Value = 10 }
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_ValidFunctionCall_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new FunctionCallExpression
        {
            Name = new QName(FunctionNamespaces.Fn, "string-length"),
            Arguments = [new StringLiteral { Value = "hello" }]
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_UndefinedVariable_Fails()
    {
        var engine = new QueryEngine();
        var expr = new VariableReference
        {
            Name = new QName(NamespaceId.None, "undefined")
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == XQueryErrorCodes.XPST0008);
    }

    [Fact]
    public void Compile_UnknownFunction_Fails()
    {
        var engine = new QueryEngine();
        var expr = new FunctionCallExpression
        {
            Name = new QName(FunctionNamespaces.Fn, "nonexistent-function"),
            Arguments = []
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == XQueryErrorCodes.XPST0017);
    }

    [Fact]
    public void Compile_WithDefaultContainer_SetsContainer()
    {
        var engine = new QueryEngine();
        var expr = new IntegerLiteral { Value = 42 };
        var options = new CompilationOptions
        {
            DefaultContainer = new ContainerId(1)
        };

        var result = engine.Compile(expr, options);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_AnalyzedExpression_IsSet()
    {
        var engine = new QueryEngine();
        var expr = new IntegerLiteral { Value = 42 };

        var result = engine.Compile(expr);

        result.AnalyzedExpression.Should().NotBeNull();
    }

    [Fact]
    public void Compile_FlworExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new FlworExpression
        {
            Clauses =
            [
                new ForClause
                {
                    Bindings =
                    [
                        new ForBinding
                        {
                            Variable = new QName(NamespaceId.None, "x"),
                            Expression = new RangeExpression
                            {
                                Start = new IntegerLiteral { Value = 1 },
                                End = new IntegerLiteral { Value = 5 }
                            }
                        }
                    ]
                }
            ],
            ReturnExpression = new VariableReference { Name = new QName(NamespaceId.None, "x") }
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_ComplexFlwor_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new FlworExpression
        {
            Clauses =
            [
                new ForClause
                {
                    Bindings =
                    [
                        new ForBinding
                        {
                            Variable = new QName(NamespaceId.None, "x"),
                            Expression = new RangeExpression
                            {
                                Start = new IntegerLiteral { Value = 1 },
                                End = new IntegerLiteral { Value = 10 }
                            }
                        }
                    ]
                },
                new LetClause
                {
                    Bindings =
                    [
                        new LetBinding
                        {
                            Variable = new QName(NamespaceId.None, "y"),
                            Expression = new BinaryExpression
                            {
                                Left = new VariableReference { Name = new QName(NamespaceId.None, "x") },
                                Operator = BinaryOperator.Multiply,
                                Right = new IntegerLiteral { Value = 2 }
                            }
                        }
                    ]
                },
                new WhereClause
                {
                    Condition = new BinaryExpression
                    {
                        Left = new VariableReference { Name = new QName(NamespaceId.None, "x") },
                        Operator = BinaryOperator.GreaterThan,
                        Right = new IntegerLiteral { Value = 5 }
                    }
                }
            ],
            ReturnExpression = new VariableReference { Name = new QName(NamespaceId.None, "y") }
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    #endregion

    #region CompilationOptions Tests

    [Fact]
    public void CompilationOptions_DefaultContainer_CanBeSet()
    {
        var options = new CompilationOptions
        {
            DefaultContainer = new ContainerId(42)
        };

        options.DefaultContainer.Value.Should().Be(42);
    }

    [Fact]
    public void CompilationOptions_EnableOptimization_DefaultsTrue()
    {
        var options = new CompilationOptions();

        options.EnableOptimization.Should().BeTrue();
    }

    [Fact]
    public void CompilationOptions_EnableOptimization_CanBeDisabled()
    {
        var options = new CompilationOptions
        {
            EnableOptimization = false
        };

        options.EnableOptimization.Should().BeFalse();
    }

    [Fact]
    public void CompilationOptions_StrictTypeChecking_DefaultsFalse()
    {
        var options = new CompilationOptions();

        options.StrictTypeChecking.Should().BeFalse();
    }

    [Fact]
    public void CompilationOptions_StrictTypeChecking_CanBeEnabled()
    {
        var options = new CompilationOptions
        {
            StrictTypeChecking = true
        };

        options.StrictTypeChecking.Should().BeTrue();
    }

    #endregion

    #region QueryCompilationResult Tests

    [Fact]
    public void QueryCompilationResult_Success_WhenNoErrors()
    {
        var result = new QueryCompilationResult
        {
            Success = true,
            Errors = []
        };

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void QueryCompilationResult_Failure_WhenHasErrors()
    {
        var result = new QueryCompilationResult
        {
            Success = false,
            Errors = [new AnalysisError("ERR001", "Error message", null)]
        };

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void QueryCompilationResult_ExecutionPlan_NullWhenFailed()
    {
        var result = new QueryCompilationResult
        {
            Success = false,
            Errors = [new AnalysisError("ERR001", "Error message", null)],
            ExecutionPlan = null
        };

        result.ExecutionPlan.Should().BeNull();
    }

    #endregion

    #region CreateContext Tests

    [Fact]
    public void CreateContext_ReturnsContext()
    {
        var engine = new QueryEngine();
        var container = new ContainerId(1);

        var context = engine.CreateContext(container, CancellationToken.None);

        context.Should().NotBeNull();
    }

    [Fact]
    public void CreateContext_WithCancellationToken_Works()
    {
        var engine = new QueryEngine();
        var container = new ContainerId(1);
        var cts = new CancellationTokenSource();

        var context = engine.CreateContext(container, cancellationToken: cts.Token);

        context.Should().NotBeNull();
    }

    #endregion

    #region Type Expression Compilation Tests

    [Fact]
    public void Compile_InstanceOfExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new InstanceOfExpression
        {
            Expression = new IntegerLiteral { Value = 42 },
            TargetType = XdmSequenceType.Integer
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_CastExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new CastExpression
        {
            Expression = new StringLiteral { Value = "42" },
            TargetType = XdmSequenceType.Integer
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_CastableExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new CastableExpression
        {
            Expression = new StringLiteral { Value = "42" },
            TargetType = XdmSequenceType.Integer
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_TreatExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new TreatExpression
        {
            Expression = new IntegerLiteral { Value = 42 },
            TargetType = XdmSequenceType.Integer
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    #endregion

    #region Conditional Expression Compilation Tests

    [Fact]
    public void Compile_IfExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new IfExpression
        {
            Condition = BooleanLiteral.True,
            Then = new StringLiteral { Value = "yes" },
            Else = new StringLiteral { Value = "no" }
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_QuantifiedExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new QuantifiedExpression
        {
            Quantifier = Quantifier.Some,
            Bindings =
            [
                new QuantifiedBinding
                {
                    Variable = new QName(NamespaceId.None, "x"),
                    Expression = new RangeExpression
                    {
                        Start = new IntegerLiteral { Value = 1 },
                        End = new IntegerLiteral { Value = 10 }
                    }
                }
            ],
            Satisfies = new BinaryExpression
            {
                Left = new VariableReference { Name = new QName(NamespaceId.None, "x") },
                Operator = BinaryOperator.GreaterThan,
                Right = new IntegerLiteral { Value = 5 }
            }
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    #endregion

    #region Path Expression Compilation Tests

    [Fact]
    public void Compile_SimplePathExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new PathExpression
        {
            IsAbsolute = false,
            Steps =
            [
                new StepExpression
                {
                    Axis = Axis.Child,
                    NodeTest = new NameTest { LocalName = "element" },
                    Predicates = []
                }
            ]
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_PathWithPredicate_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new PathExpression
        {
            IsAbsolute = true,
            Steps =
            [
                new StepExpression
                {
                    Axis = Axis.Child,
                    NodeTest = new NameTest { LocalName = "item" },
                    Predicates = [new IntegerLiteral { Value = 1 }]
                }
            ]
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_FilterExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new FilterExpression
        {
            Primary = new RangeExpression
            {
                Start = new IntegerLiteral { Value = 1 },
                End = new IntegerLiteral { Value = 10 }
            },
            Predicates = [new IntegerLiteral { Value = 5 }]
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    #endregion

    #region Unary Expression Compilation Tests

    [Fact]
    public void Compile_UnaryMinus_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new UnaryExpression
        {
            Operator = UnaryOperator.Minus,
            Operand = new IntegerLiteral { Value = 42 }
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_UnaryPlus_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new UnaryExpression
        {
            Operator = UnaryOperator.Plus,
            Operand = new IntegerLiteral { Value = 42 }
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    #endregion

    #region Multiple Function Calls Compilation Tests

    [Fact]
    public void Compile_NestedFunctionCalls_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new FunctionCallExpression
        {
            Name = new QName(FunctionNamespaces.Fn, "upper-case"),
            Arguments =
            [
                new FunctionCallExpression
                {
                    Name = new QName(FunctionNamespaces.Fn, "lower-case"),
                    Arguments = [new StringLiteral { Value = "TEST" }]
                }
            ]
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_FunctionCallWithMultipleArgs_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new FunctionCallExpression
        {
            Name = new QName(FunctionNamespaces.Fn, "concat"),
            Arguments =
            [
                new StringLiteral { Value = "hello" },
                new StringLiteral { Value = " world" }
            ]
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    #endregion

    #region Complex Expression Compilation Tests

    [Fact]
    public void Compile_StringConcatExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new StringConcatExpression
        {
            Operands =
            [
                new StringLiteral { Value = "hello" },
                new StringLiteral { Value = " " },
                new StringLiteral { Value = "world" }
            ]
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_SimpleMapExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new SimpleMapExpression
        {
            Left = new RangeExpression
            {
                Start = new IntegerLiteral { Value = 1 },
                End = new IntegerLiteral { Value = 5 }
            },
            Right = ContextItemExpression.Instance
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_ArrowExpression_Succeeds()
    {
        var engine = new QueryEngine();
        var expr = new ArrowExpression
        {
            Expression = new StringLiteral { Value = "hello" },
            FunctionCall = new FunctionCallExpression
            {
                Name = new QName(FunctionNamespaces.Fn, "upper-case"),
                Arguments = [new StringLiteral { Value = "placeholder" }]
            }
        };

        var result = engine.Compile(expr);

        result.Success.Should().BeTrue();
    }

    #endregion

    #region FunctionLibrary Integration Tests

    [Fact]
    public void FunctionLibrary_Standard_ContainsAllExpectedFunctions()
    {
        var lib = FunctionLibrary.Standard;

        // String functions
        lib.Resolve(new QName(FunctionNamespaces.Fn, "string-length"), 1).Should().NotBeNull();
        lib.Resolve(new QName(FunctionNamespaces.Fn, "substring"), 2).Should().NotBeNull();
        lib.Resolve(new QName(FunctionNamespaces.Fn, "concat"), 2).Should().NotBeNull();
        lib.Resolve(new QName(FunctionNamespaces.Fn, "contains"), 2).Should().NotBeNull();

        // Numeric functions
        lib.Resolve(new QName(FunctionNamespaces.Fn, "abs"), 1).Should().NotBeNull();
        lib.Resolve(new QName(FunctionNamespaces.Fn, "sum"), 1).Should().NotBeNull();
        lib.Resolve(new QName(FunctionNamespaces.Fn, "count"), 1).Should().NotBeNull();

        // Sequence functions
        lib.Resolve(new QName(FunctionNamespaces.Fn, "empty"), 1).Should().NotBeNull();
        lib.Resolve(new QName(FunctionNamespaces.Fn, "exists"), 1).Should().NotBeNull();
        lib.Resolve(new QName(FunctionNamespaces.Fn, "head"), 1).Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_Register_AddsCustomFunction()
    {
        var lib = new FunctionLibrary();

        lib.Register(new StringLengthFunction());

        lib.Resolve(new QName(FunctionNamespaces.Fn, "string-length"), 1).Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_GetAllFunctions_ReturnsAll()
    {
        var lib = new FunctionLibrary();
        lib.Register(new StringLengthFunction());
        lib.Register(new ContainsFunction());

        lib.GetAllFunctions().Should().HaveCount(2);
    }

    #endregion
}
