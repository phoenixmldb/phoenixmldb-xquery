using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Optimizes XQuery expressions and produces execution plans.
/// </summary>
public sealed class QueryOptimizer
{
    private readonly IQueryPlanOptimizer? _planOptimizer;

    public QueryOptimizer(IQueryPlanOptimizer? planOptimizer = null)
    {
        _planOptimizer = planOptimizer;
    }

    /// <summary>
    /// Optimizes an expression and produces an execution plan.
    /// </summary>
    public ExecutionPlan Optimize(XQueryExpression expression, OptimizationContext context)
    {
        // Phase 1: Logical optimization (AST rewrites)
        expression = ApplyLogicalOptimizations(expression, context.BackwardsCompatible);

        // Phase 2: Physical planning
        var rootOperator = CreatePhysicalPlan(expression, context);

        // Phase 3: Cost estimation
        var cost = EstimateCost(rootOperator);
        var cardinality = EstimateCardinality(rootOperator);

        var mainModule = expression as Ast.ModuleExpression;
        var mainBaseUri = mainModule?.BaseUri;
        var copyNsMode = mainModule?.CopyNamespacesMode ?? Analysis.CopyNamespacesMode.PreserveInherit;
        return new ExecutionPlan
        {
            Root = rootOperator,
            OriginalExpression = expression,
            EstimatedCost = cost,
            EstimatedCardinality = cardinality,
            DeclaredBaseUri = mainBaseUri,
            DeclaredCopyNamespacesMode = copyNsMode
        };
    }

    /// <summary>
    /// Applies logical optimizations to the AST.
    /// </summary>
    private static XQueryExpression ApplyLogicalOptimizations(XQueryExpression expression, bool backwardsCompatible = false)
    {
        // Constant folding
        var folder = new ConstantFolder { BackwardsCompatible = backwardsCompatible };
        expression = folder.Rewrite(expression);

        // Predicate simplification
        var simplifier = new PredicateSimplifier();
        expression = simplifier.Rewrite(expression);

        return expression;
    }

    /// <summary>
    /// Creates a physical operator tree from the expression.
    /// </summary>
    private PhysicalOperator CreatePhysicalPlan(XQueryExpression expression, OptimizationContext context)
    {
        return expression switch
        {
            PathExpression path => PlanPathExpression(path, context),
            FlworExpression flwor => PlanFlworExpression(flwor, context),
            FunctionCallExpression func => PlanFunctionCall(func, context),
            BinaryExpression binary => PlanBinaryExpression(binary, context),
            UnaryExpression unary => PlanUnaryExpression(unary, context),
            IfExpression @if => PlanIfExpression(@if, context),
            SequenceExpression seq => PlanSequenceExpression(seq, context),
            IntegerLiteral il => new ConstantOperator { Value = il.Value },
            DecimalLiteral dl => new ConstantOperator { Value = dl.Value },
            DoubleLiteral dbl => new ConstantOperator { Value = dbl.Value },
            StringLiteral sl => new ConstantOperator { Value = sl.Value },
            BooleanLiteral bl => new ConstantOperator { Value = bl.Value },
            EmptySequence => new EmptyOperator(),
            ContextItemDeclarationExpression ctxDecl =>
                new ContextItemDeclarationOperator
                {
                    ValueOperator = ctxDecl.DefaultValue != null ? CreatePhysicalPlan(ctxDecl.DefaultValue, context) : null,
                    TypeConstraint = ctxDecl.TypeConstraint,
                    IsExternal = ctxDecl.IsExternal
                },
            DecimalFormatDeclarationExpression => new EmptyOperator(),
            ContextItemExpression => new ContextItemOperator(),
            VariableReference vr => new VariableOperator { VariableName = vr.Name },
            FilterExpression filter => PlanFilterExpression(filter, context),
            ElementConstructor elem => PlanElementConstructor(elem, context),
            RangeExpression range => new RangeOperator
            {
                Start = CreatePhysicalPlan(range.Start, context),
                End = CreatePhysicalPlan(range.End, context)
            },
            CastExpression cast => new CastOperator
            {
                Operand = CreatePhysicalPlan(cast.Expression, context),
                TargetType = cast.TargetType,
                OperandIsStringLiteral = cast.Expression is StringLiteral
            },
            CastableExpression castable => new CastableOperator
            {
                Operand = CreatePhysicalPlan(castable.Expression, context),
                TargetType = castable.TargetType,
                OperandIsStringLiteral = castable.Expression is StringLiteral
            },
            InstanceOfExpression inst => new InstanceOfOperator
            {
                Operand = CreatePhysicalPlan(inst.Expression, context),
                TargetType = inst.TargetType
            },
            TreatExpression treat => new TreatOperator
            {
                Operand = CreatePhysicalPlan(treat.Expression, context),
                TargetType = treat.TargetType
            },
            StringConcatExpression strConcat => new StringConcatOperator
            {
                Operands = strConcat.Operands.Select(o => CreatePhysicalPlan(o, context)).ToList()
            },
            QuantifiedExpression quant => new QuantifiedOperator
            {
                Quantifier = quant.Quantifier,
                Bindings = quant.Bindings.Select(b => new QuantifiedBindingOperator
                {
                    Variable = b.Variable,
                    TypeDeclaration = b.TypeDeclaration,
                    InputOperator = CreatePhysicalPlan(b.Expression, context)
                }).ToList(),
                Satisfies = CreatePhysicalPlan(quant.Satisfies, context)
            },
            TryCatchExpression tryCatch => new TryCatchOperator
            {
                TryOperator = CreatePhysicalPlan(tryCatch.TryExpression, context),
                CatchClauses = tryCatch.CatchClauses.Select(c => new CatchClauseOperator
                {
                    ErrorCodes = c.ErrorCodes,
                    ResultOperator = CreatePhysicalPlan(c.Expression, context)
                }).ToList(),
                ErrorNamespaceId = context.StaticContext?.Namespaces.GetOrCreateId("http://www.w3.org/2005/xqt-errors")
                    ?? NamespaceId.None
            },
            SimpleMapExpression simpleMap => CreateSimpleMapPlan(simpleMap, context),
            SwitchExpression sw => new SwitchOperator
            {
                Operand = CreatePhysicalPlan(sw.Operand, context),
                Cases = sw.Cases.Select(c => new SwitchCaseOperator
                {
                    Values = c.Values.Select(v => CreatePhysicalPlan(v, context)).ToList(),
                    Result = CreatePhysicalPlan(c.Result, context)
                }).ToList(),
                Default = CreatePhysicalPlan(sw.Default, context)
            },
            TypeswitchExpression ts => new TypeswitchOperator
            {
                Operand = CreatePhysicalPlan(ts.Operand, context),
                Cases = ts.Cases.Select(c => new TypeswitchCaseOperator
                {
                    Variable = c.Variable,
                    Types = c.Types,
                    Result = CreatePhysicalPlan(c.Result, context)
                }).ToList(),
                DefaultVariable = ts.Default.Variable,
                DefaultResult = CreatePhysicalPlan(ts.Default.Result, context)
            },
            MapConstructor map => new MapConstructorOperator
            {
                Entries = map.Entries.Select(e => new MapEntryOperator
                {
                    Key = CreatePhysicalPlan(e.Key, context),
                    Value = CreatePhysicalPlan(e.Value, context)
                }).ToList()
            },
            ArrayConstructor arr => new ArrayConstructorOperator
            {
                Kind = arr.Kind,
                Members = arr.Members.Select(m => CreatePhysicalPlan(m, context)).ToList()
            },
            LookupExpression lookup => new LookupOperator
            {
                Base = CreatePhysicalPlan(lookup.Base, context),
                Key = lookup.Key != null ? CreatePhysicalPlan(lookup.Key, context) : null
            },
            UnaryLookupExpression unaryLookup => new UnaryLookupOperator
            {
                Key = unaryLookup.Key != null ? CreatePhysicalPlan(unaryLookup.Key, context) : null
            },
            ArrowExpression arrow => arrow.IsThinArrow
                ? new ThinArrowOperator
                {
                    Expression = CreatePhysicalPlan(arrow.Expression, context),
                    FunctionCall = CreatePhysicalPlan(arrow.FunctionCall, context)
                }
                : CreatePhysicalPlan(arrow.FunctionCall, context),
            NamedFunctionRef nfr => new NamedFunctionRefOperator { Name = nfr.Name, Arity = nfr.Arity },
            InlineFunctionExpression inline => new InlineFunctionOperator
            {
                Parameters = inline.Parameters,
                Body = inline.Body,
                DeclaredReturnType = inline.ReturnType
            },
            DynamicFunctionCallExpression dfc when dfc.Arguments.Any(a => a is ArgumentPlaceholder) =>
                PlanDynamicPartialApplication(dfc, context),
            DynamicFunctionCallExpression dfc => new DynamicFunctionCallOperator
            {
                FunctionExpression = CreatePhysicalPlan(dfc.FunctionExpression, context),
                Arguments = dfc.Arguments.Select(a => CreatePhysicalPlan(a, context)).ToList()
            },
            ModuleExpression mod => PlanModuleExpression(mod, context),
            VariableDeclarationExpression varDecl => new VariableDeclarationOperator
            {
                VariableName = varDecl.Name,
                ValueOperator = varDecl.Value != null ? CreatePhysicalPlan(varDecl.Value, context) : null,
                IsExternal = varDecl.IsExternal,
                TypeDeclaration = varDecl.TypeDeclaration
            },
            FunctionDeclarationExpression funcDecl => new FunctionDeclarationOperator
            {
                FunctionName = funcDecl.Name,
                Parameters = funcDecl.Parameters,
                Body = funcDecl.Body,
                DeclaredReturnType = funcDecl.ReturnType,
                ModuleBaseUri = funcDecl.ModuleBaseUri
            },
            NamespaceDeclarationExpression => new EmptyOperator(), // Namespace declarations handled statically
            ModuleImportExpression => new EmptyOperator(), // Module imports resolved during static analysis
            SchemaImportExpression => new EmptyOperator(), // Schema imports resolved during static analysis

            // XQuery 3.1/4.0: string constructor
            StringConstructorExpression strCtor => new StringConstructorOperator
            {
                Parts = strCtor.Parts.Select(p => p switch
                {
                    StringConstructorLiteralPart lit => new StringConstructorPartOp { LiteralValue = lit.Value },
                    StringConstructorInterpolationPart interp => new StringConstructorPartOp { ExpressionOperator = CreatePhysicalPlan(interp.Expression, context) },
                    _ => new StringConstructorPartOp { LiteralValue = "" }
                }).ToList()
            },

            // XPath 4.0: record constructor → map with string keys
            RecordConstructorExpression record => new RecordConstructorOperator
            {
                Fields = record.Fields.Select(f => (f.Name, CreatePhysicalPlan(f.Value, context))).ToList()
            },

            // XQuery Full-Text
            FtContainsExpression ftContains => new FtContainsOperator
            {
                Source = CreatePhysicalPlan(ftContains.Source, context),
                Selection = ftContains.Selection,
                MatchOptions = ftContains.MatchOptions
            },

            // XQuery Update Facility
            InsertExpression insert => new InsertOperator
            {
                Source = CreatePhysicalPlan(insert.Source, context),
                Target = CreatePhysicalPlan(insert.Target, context),
                Position = insert.Position
            },
            DeleteExpression delete => new DeleteOperator
            {
                Target = CreatePhysicalPlan(delete.Target, context)
            },
            ReplaceNodeExpression replace => new ReplaceNodeOperator
            {
                Target = CreatePhysicalPlan(replace.Target, context),
                Replacement = CreatePhysicalPlan(replace.Replacement, context)
            },
            ReplaceValueExpression replaceVal => new ReplaceValueOperator
            {
                Target = CreatePhysicalPlan(replaceVal.Target, context),
                Value = CreatePhysicalPlan(replaceVal.Value, context)
            },
            RenameExpression rename => new RenameOperator
            {
                Target = CreatePhysicalPlan(rename.Target, context),
                NewName = CreatePhysicalPlan(rename.NewName, context)
            },
            TransformExpression transform => new TransformOperator
            {
                CopyBindings = transform.CopyBindings.Select(b => new TransformCopyBindingOperator
                {
                    Variable = b.Variable,
                    Expression = CreatePhysicalPlan(b.Expression, context)
                }).ToList(),
                ModifyExpr = CreatePhysicalPlan(transform.ModifyExpr, context),
                ReturnExpr = CreatePhysicalPlan(transform.ReturnExpr, context)
            },

            // Node constructors
            AttributeConstructor attrCtor => new AttributeConstructorOperator
            {
                Name = attrCtor.Name,
                ValueOperator = CreatePhysicalPlan(attrCtor.Value, context)
            },
            ComputedElementConstructor compElem => new ComputedElementConstructorOperator
            {
                NameOperator = compElem.StaticName != null
                    ? new ConstantOperator { Value = compElem.StaticName }
                    : CreatePhysicalPlan(compElem.NameExpression, context),
                ContentOperator = CreatePhysicalPlan(compElem.ContentExpression, context)
            },
            ComputedAttributeConstructor compAttr => new ComputedAttributeConstructorOperator
            {
                NameOperator = compAttr.StaticName != null
                    ? new ConstantOperator { Value = compAttr.StaticName }
                    : CreatePhysicalPlan(compAttr.NameExpression, context),
                ValueOperator = CreatePhysicalPlan(compAttr.ValueExpression, context)
            },
            TextConstructor textCtor => new TextConstructorOperator
            {
                ContentOperator = CreatePhysicalPlan(textCtor.Value, context)
            },
            CommentConstructor commentCtor => new CommentConstructorOperator
            {
                ContentOperator = CreatePhysicalPlan(commentCtor.Value, context)
            },
            PIConstructor piCtor => new PIConstructorOperator
            {
                DirectTarget = piCtor.DirectTarget,
                TargetOperator = piCtor.TargetExpression != null ? CreatePhysicalPlan(piCtor.TargetExpression, context) : null,
                ContentOperator = CreatePhysicalPlan(piCtor.Value, context)
            },
            DocumentConstructor docCtor => new DocumentConstructorOperator
            {
                ContentOperator = CreatePhysicalPlan(docCtor.Content, context)
            },
            NamespaceConstructor nsCtor => new NamespaceNodeOperator
            {
                DirectPrefix = nsCtor.DirectPrefix,
                PrefixOperator = nsCtor.PrefixExpression != null ? CreatePhysicalPlan(nsCtor.PrefixExpression, context) : null,
                UriOperator = CreatePhysicalPlan(nsCtor.UriExpression, context)
            },
            ValidateExpression validate => new ValidateOperator
            {
                ExpressionOperator = CreatePhysicalPlan(validate.Expression, context),
                Mode = validate.Mode,
                TypeName = validate.TypeName
            },

            _ => throw new InvalidOperationException(
                $"No physical operator mapping for AST expression type '{expression.GetType().Name}'. " +
                $"This is a compiler bug — all expression types must be mapped in QueryOptimizer.CreatePhysicalPlan.")
        };
    }

    private PhysicalOperator PlanPathExpression(PathExpression path, OptimizationContext context)
    {
        PhysicalOperator? current = null;

        // Check if an external optimizer can produce a better plan (e.g. index scan)
        if (_planOptimizer != null && path.InitialExpression == null)
        {
            var optimized = _planOptimizer.OptimizePath(path, context.Container);
            if (optimized != null)
            {
                current = optimized;
            }
        }

        // Build step-by-step navigation
        foreach (var step in path.Steps)
        {
            if (current == null)
            {
                // Start from InitialExpression if present (e.g., $var/path), otherwise document root or context
                if (path.InitialExpression != null)
                {
                    current = CreatePhysicalPlan(path.InitialExpression, context);
                }
                else
                {
                    current = path.IsAbsolute
                        ? new DocumentRootOperator { Container = context.Container }
                        : new ContextItemOperator();
                }
            }

            // Per XPath spec, positional predicates on a step must be evaluated
            // per-input-node. E.g., chapter//footnote[1] means "first footnote child
            // of each node in chapter//", not "first footnote overall".
            // Use PerNodeStepOperator when any predicate is positional.
            var hasPositionalPred = step.Predicates.Any(PredicateUsesPositionalAccess);
            if (step.Predicates.Count > 0 && hasPositionalPred)
            {
                current = new PerNodeStepOperator
                {
                    Input = current,
                    Axis = step.Axis,
                    NodeTest = step.NodeTest,
                    PredicateOperators = step.Predicates.Select(p => CreatePhysicalPlan(p, context)).ToList(),
                    PredicatePositional = step.Predicates.Select(PredicateUsesPositionalAccess).ToList()
                };
            }
            else
            {
                current = new AxisNavigationOperator
                {
                    Input = current,
                    Axis = step.Axis,
                    NodeTest = step.NodeTest
                };

                // Apply non-positional predicates
                foreach (var predicate in step.Predicates)
                {
                    current = new FilterOperator
                    {
                        Input = current,
                        PredicateOperator = CreatePhysicalPlan(predicate, context),
                        RequiresPositionalAccess = false
                    };
                }
            }

            // Per XPath spec: "Every path expression returns its result nodes in document order."
            // Reverse axes (ancestor, preceding, preceding-sibling) enumerate in reverse document
            // order for correct predicate position semantics, but the final result must be sorted
            // into document order.
            if (step.Axis is Axis.Ancestor or Axis.AncestorOrSelf
                or Axis.Preceding or Axis.PrecedingSibling)
            {
                current = new DocumentOrderSortOperator { Input = current };
            }
        }

        if (current != null)
            return current;

        // No steps — return the base of the path
        if (path.InitialExpression != null)
            return CreatePhysicalPlan(path.InitialExpression, context);
        return path.IsAbsolute
            ? new DocumentRootOperator { Container = context.Container }
            : new EmptyOperator();
    }

    private PhysicalOperator PlanFlworExpression(FlworExpression flwor, OptimizationContext context)
    {
        var clauses = new List<FlworClauseOperator>();

        foreach (var clause in flwor.Clauses)
        {
            clauses.Add(clause switch
            {
                ForClause fc => new ForClauseOperator
                {
                    IsMember = fc.IsMember,
                    Bindings = fc.Bindings.Select(b => new ForBindingOperator
                    {
                        Variable = b.Variable,
                        PositionalVariable = b.PositionalVariable,
                        AllowingEmpty = b.AllowingEmpty,
                        InputOperator = CreatePhysicalPlan(b.Expression, context),
                        TypeDeclaration = b.TypeDeclaration
                    }).ToList()
                },
                LetClause lc => new LetClauseOperator
                {
                    Bindings = lc.Bindings.Select(b => new LetBindingOperator
                    {
                        Variable = b.Variable,
                        InputOperator = CreatePhysicalPlan(b.Expression, context),
                        TypeDeclaration = b.TypeDeclaration
                    }).ToList()
                },
                WhereClause wc => new WhereClauseOperator
                {
                    ConditionOperator = CreatePhysicalPlan(wc.Condition, context)
                },
                OrderByClause obc => new OrderByClauseOperator
                {
                    Stable = obc.Stable,
                    OrderSpecs = obc.OrderSpecs.Select(s => new OrderSpecOperator
                    {
                        KeyOperator = CreatePhysicalPlan(s.Expression, context),
                        Direction = s.Direction,
                        EmptyOrder = s.EmptyOrder,
                        Collation = s.Collation
                    }).ToList()
                },
                GroupByClause gbc => new GroupByClauseOperator
                {
                    GroupingSpecs = gbc.GroupingSpecs.Select(s => new GroupingSpecOperator
                    {
                        Variable = s.Variable,
                        KeyOperator = s.Expression != null ? CreatePhysicalPlan(s.Expression, context) : null,
                        TypeDeclaration = s.TypeDeclaration,
                        Collation = s.Collation
                    }).ToList()
                },
                CountClause cc => new CountClauseOperator { Variable = cc.Variable },
                WindowClause wc => new WindowClauseOperator
                {
                    Kind = wc.Kind,
                    Variable = wc.Variable,
                    TypeDeclaration = wc.TypeDeclaration,
                    OnlyEnd = wc.OnlyEnd,
                    InputOperator = CreatePhysicalPlan(wc.Expression, context),
                    StartCondition = PlanWindowCondition(wc.Start, context),
                    EndCondition = wc.End != null ? PlanWindowCondition(wc.End, context) : null
                },
                _ => throw new NotSupportedException($"FLWOR clause type {clause.GetType().Name} not supported")
            });
        }

        return new FlworOperator
        {
            Clauses = clauses,
            ReturnOperator = CreatePhysicalPlan(flwor.ReturnExpression, context),
            OtherwiseOperator = flwor.OtherwiseExpression != null
                ? CreatePhysicalPlan(flwor.OtherwiseExpression, context) : null
        };
    }

    private WindowConditionOperator PlanWindowCondition(WindowCondition cond, OptimizationContext context)
    {
        return new WindowConditionOperator
        {
            CurrentItem = cond.CurrentItem,
            PreviousItem = cond.PreviousItem,
            NextItem = cond.NextItem,
            Position = cond.Position,
            WhenOperator = CreatePhysicalPlan(cond.When, context)
        };
    }

    private PhysicalOperator PlanFunctionCall(FunctionCallExpression func, OptimizationContext context)
    {
        // XPath 4.0: resolve keyword arguments to positional before planning
        if (func.Arguments.Any(a => a is KeywordArgument))
            ResolveKeywordArguments(func, context);

        // Check for partial application: any argument is ArgumentPlaceholder (?)
        if (func.Arguments.Any(a => a is ArgumentPlaceholder))
        {
            // Build a partial application operator
            var fixedArgs = new List<(int index, PhysicalOperator op)?>();
            int placeholderCount = 0;
            for (int i = 0; i < func.Arguments.Count; i++)
            {
                if (func.Arguments[i] is ArgumentPlaceholder)
                {
                    fixedArgs.Add(null); // placeholder
                    placeholderCount++;
                }
                else
                {
                    fixedArgs.Add((i, CreatePhysicalPlan(func.Arguments[i], context)));
                }
            }
            return new PartialApplicationOperator
            {
                ResolvedFunc = func.ResolvedFunction,
                FuncName = func.Name,
                ArgumentSlots = fixedArgs,
                TotalArity = func.Arguments.Count,
                PlaceholderCount = placeholderCount
            };
        }

        var argOperators = func.Arguments.Select(a => CreatePhysicalPlan(a, context)).ToList();

        return new FunctionCallOperator
        {
            Function = func.ResolvedFunction,
            FunctionName = func.Name,
            ArgumentOperators = argOperators
        };
    }

    /// <summary>
    /// XPath 4.0: rewrites keyword arguments in a function call to positional order.
    /// Uses ResolvedFunction (from static analysis) or FunctionLibrary (from context) to look up parameter names.
    /// </summary>
    private static void ResolveKeywordArguments(FunctionCallExpression func, OptimizationContext context)
    {
        // Find the function's parameter definitions
        var resolved = func.ResolvedFunction
            ?? context.FunctionLibrary?.Resolve(func.Name, func.Arguments.Count);
        if (resolved == null)
            throw new InvalidOperationException(
                $"Cannot resolve keyword arguments for {func.Name.LocalName}: function not found");

        var parameters = resolved.Parameters;
        var positional = new List<XQueryExpression>();
        var keywords = new Dictionary<string, XQueryExpression>();

        foreach (var arg in func.Arguments)
        {
            if (arg is KeywordArgument kw)
                keywords[kw.Name] = kw.Value;
            else
                positional.Add(arg);
        }

        var reordered = new XQueryExpression[parameters.Count];
        for (var i = 0; i < positional.Count && i < parameters.Count; i++)
            reordered[i] = positional[i];

        foreach (var (name, value) in keywords)
        {
            var idx = -1;
            for (var i = 0; i < parameters.Count; i++)
            {
                if (parameters[i].Name.LocalName == name)
                { idx = i; break; }
            }
            if (idx < 0)
                throw new InvalidOperationException(
                    $"XPST0017: Unknown parameter '{name}' for function {func.Name.LocalName}");
            if (reordered[idx] != null)
                throw new InvalidOperationException(
                    $"XPST0003: Parameter '{name}' is already supplied by a positional argument");
            reordered[idx] = value;
        }

        func.Arguments = reordered.ToList();
    }

    private PhysicalOperator PlanDynamicPartialApplication(DynamicFunctionCallExpression dfc, OptimizationContext context)
    {
        var slots = new List<(int index, PhysicalOperator op)?>();
        int placeholders = 0;
        for (int i = 0; i < dfc.Arguments.Count; i++)
        {
            if (dfc.Arguments[i] is ArgumentPlaceholder)
            {
                slots.Add(null);
                placeholders++;
            }
            else
            {
                slots.Add((i, CreatePhysicalPlan(dfc.Arguments[i], context)));
            }
        }
        return new DynamicPartialApplicationOperator
        {
            FuncExpression = CreatePhysicalPlan(dfc.FunctionExpression, context),
            ArgumentSlots = slots,
            TotalArity = dfc.Arguments.Count,
            PlaceholderCount = placeholders
        };
    }

    private PhysicalOperator PlanBinaryExpression(BinaryExpression binary, OptimizationContext context)
    {
        var leftOp = CreatePhysicalPlan(binary.Left, context);
        var rightOp = CreatePhysicalPlan(binary.Right, context);

        return new BinaryOperatorNode
        {
            Left = leftOp,
            Right = rightOp,
            Operator = binary.Operator
        };
    }

    private PhysicalOperator PlanUnaryExpression(UnaryExpression unary, OptimizationContext context)
    {
        var operandOp = CreatePhysicalPlan(unary.Operand, context);

        return new UnaryOperatorNode
        {
            Operand = operandOp,
            Operator = unary.Operator
        };
    }

    private PhysicalOperator PlanIfExpression(IfExpression @if, OptimizationContext context)
    {
        return new IfOperator
        {
            Condition = CreatePhysicalPlan(@if.Condition, context),
            Then = CreatePhysicalPlan(@if.Then, context),
            Else = @if.Else != null
                ? CreatePhysicalPlan(@if.Else, context)
                : new EmptyOperator()
        };
    }

    private PhysicalOperator PlanSequenceExpression(SequenceExpression seq, OptimizationContext context)
    {
        var itemOps = seq.Items.Select(i => CreatePhysicalPlan(i, context)).ToList();
        return new SequenceOperator { Items = itemOps };
    }

    private PhysicalOperator PlanFilterExpression(FilterExpression filter, OptimizationContext context)
    {
        var primary = CreatePhysicalPlan(filter.Primary, context);

        foreach (var predicate in filter.Predicates)
        {
            primary = new FilterOperator
            {
                Input = primary,
                PredicateOperator = CreatePhysicalPlan(predicate, context),
                RequiresPositionalAccess = PredicateUsesPositionalAccess(predicate)
            };
        }

        return primary;
    }

    private PhysicalOperator PlanModuleExpression(ModuleExpression mod, OptimizationContext context)
    {
        // Override boundary-space mode from prolog declaration if present
        if (mod.BoundarySpacePreserve.HasValue)
        {
            context.BoundarySpacePreserve = mod.BoundarySpacePreserve.Value;
        }

        // Collect namespace bindings from prolog for runtime use (computed constructors).
        // Seed with default XQuery statically-known namespace prefixes (XQuery 3.1 §2.1.1).
        var nsBindings = new Dictionary<string, string>
        {
            ["xml"] = "http://www.w3.org/XML/1998/namespace",
            ["xs"] = "http://www.w3.org/2001/XMLSchema",
            ["xsi"] = "http://www.w3.org/2001/XMLSchema-instance",
            ["fn"] = "http://www.w3.org/2005/xpath-functions",
            ["math"] = "http://www.w3.org/2005/xpath-functions/math",
            ["array"] = "http://www.w3.org/2005/xpath-functions/array",
            ["map"] = "http://www.w3.org/2005/xpath-functions/map",
            ["local"] = "http://www.w3.org/2005/xquery-local-functions"
        };
        // Collect decimal-format declarations from prolog
        Dictionary<string, Analysis.DecimalFormatProperties>? decimalFormats = null;

        foreach (var decl in mod.Declarations)
        {
            if (decl is NamespaceDeclarationExpression nsDecl)
            {
                nsBindings[nsDecl.Prefix] = nsDecl.Uri;
            }
            else if (decl is DecimalFormatDeclarationExpression dfDecl)
            {
                decimalFormats ??= new();
                var props = Analysis.DecimalFormatProperties.FromDictionary(dfDecl.Properties);
                // Expand prefixed format name (e.g., "a:test") to EQName ("Q{uri}test")
                // so runtime lookup can match regardless of which prefix is used.
                var fmtName = dfDecl.FormatName ?? "";
                if (!string.IsNullOrEmpty(fmtName) && !fmtName.StartsWith("Q{", StringComparison.Ordinal))
                {
                    var colonIdx = fmtName.IndexOf(':');
                    if (colonIdx > 0 && colonIdx < fmtName.Length - 1)
                    {
                        var prefix = fmtName[..colonIdx];
                        var local = fmtName[(colonIdx + 1)..];
                        if (nsBindings.TryGetValue(prefix, out var uri))
                            fmtName = $"Q{{{uri}}}{local}";
                    }
                }
                decimalFormats[fmtName] = props;
            }
        }

        var declOps = mod.Declarations.Select(d => CreatePhysicalPlan(d, context)).ToList();
        var bodyOp = CreatePhysicalPlan(mod.Body, context);
        return new ModuleOperator
        {
            Declarations = declOps,
            Body = bodyOp,
            NamespaceBindings = nsBindings,
            DecimalFormats = decimalFormats,
            DefaultCollation = mod.DefaultCollation
        };
    }

    private PhysicalOperator PlanElementConstructor(ElementConstructor elem, OptimizationContext context)
    {
        var attrOps = elem.Attributes.Select(a => CreatePhysicalPlan(a, context)).ToList();

        // Filter boundary whitespace per XQuery 3.1 §3.7.1.4:
        // When boundary-space policy is "strip" (the default), whitespace-only text nodes
        // adjacent to enclosed expressions or at element boundaries are removed.
        // When "preserve", all whitespace text is kept.
        var filteredContent = context.BoundarySpacePreserve
            ? elem.Content
            : FilterBoundaryWhitespace(elem.Content);

        // Tag direct child element constructors so the runtime can skip copy-namespaces
        // for them. The AST flags direct children (not inside enclosed expressions) via
        // ElementConstructor.IsDirectChild, set by the parser.
        var contentOps = new List<PhysicalOperator>(filteredContent.Count);
        foreach (var c in filteredContent)
        {
            var op = CreatePhysicalPlan(c, context);
            if (c is ElementConstructor { IsDirectChild: true } && op is ElementConstructorOperator eco)
            {
                op = new ElementConstructorOperator
                {
                    Name = eco.Name,
                    AttributeOperators = eco.AttributeOperators,
                    ContentOperators = eco.ContentOperators,
                    IsDirectChild = true
                };
            }
            contentOps.Add(op);
        }

        return new ElementConstructorOperator
        {
            Name = elem.Name,
            AttributeOperators = attrOps,
            ContentOperators = contentOps
        };
    }

    private static IReadOnlyList<XQueryExpression> FilterBoundaryWhitespace(IReadOnlyList<XQueryExpression> content)
    {
        // XQuery 3.1 §3.7.1.4: Boundary whitespace is whitespace-only text delimited by
        // the start/end of content, element/comment/PI constructors, or enclosed expressions.
        // Whitespace adjacent to character references, entity references, or CDATA sections
        // (represented as StringLiteral with ContainsCharacterReferences) is NOT boundary.
        var filtered = new List<XQueryExpression>();
        for (var i = 0; i < content.Count; i++)
        {
            var item = content[i];
            if (item is StringLiteral sl && string.IsNullOrWhiteSpace(sl.Value)
                && !sl.ContainsCharacterReferences)
            {
                bool prevIsBoundary = i == 0;
                bool nextIsBoundary = i == content.Count - 1;
                if (i > 0 && IsBoundaryDelimiter(content[i - 1])) prevIsBoundary = true;
                if (i < content.Count - 1 && IsBoundaryDelimiter(content[i + 1])) nextIsBoundary = true;
                if (prevIsBoundary && nextIsBoundary)
                    continue;
            }
            filtered.Add(item);
        }
        return filtered;
    }

    /// <summary>
    /// Checks if a content expression is a boundary delimiter for whitespace stripping.
    /// Element/comment/PI constructors and enclosed expressions are delimiters.
    /// Plain string literals (text content) and character-reference content are NOT.
    /// </summary>
    private static bool IsBoundaryDelimiter(XQueryExpression expr)
    {
        // StringLiterals are text content, not boundary delimiters
        if (expr is StringLiteral)
            return false;
        // Everything else (element constructors, enclosed expressions, etc.) is a boundary
        return true;
    }

    private static double EstimateCost(PhysicalOperator op)
    {
        // Basic cost model
        return op switch
        {
            ConstantOperator => 1,
            EmptyOperator => 1,
            ContextItemOperator => 1,
            VariableOperator => 1,
            DocumentRootOperator => 10,
            AxisNavigationOperator nav => 50 + EstimateCost(nav.Input),
            FilterOperator filter => EstimateCost(filter.Input) * 1.5,
            FlworOperator flwor => 100 + flwor.Clauses.Sum(c => EstimateClauseCost(c)),
            FunctionCallOperator func => 10 + func.ArgumentOperators.Sum(EstimateCost),
            BinaryOperatorNode bin => 5 + EstimateCost(bin.Left) + EstimateCost(bin.Right),
            UnaryOperatorNode unary => 2 + EstimateCost(unary.Operand),
            IfOperator @if => EstimateCost(@if.Condition) + Math.Max(EstimateCost(@if.Then), EstimateCost(@if.Else)),
            SequenceOperator seq => seq.Items.Sum(EstimateCost),
            _ => 100
        };
    }

    private static double EstimateClauseCost(FlworClauseOperator clause)
    {
        return clause switch
        {
            ForClauseOperator fc => fc.Bindings.Sum(b => EstimateCost(b.InputOperator)) * 10,
            LetClauseOperator lc => lc.Bindings.Sum(b => EstimateCost(b.InputOperator)),
            WhereClauseOperator wc => EstimateCost(wc.ConditionOperator),
            OrderByClauseOperator obc => obc.OrderSpecs.Sum(s => EstimateCost(s.KeyOperator)) * 5,
            _ => 10
        };
    }

    private static long EstimateCardinality(PhysicalOperator op)
    {
        return op switch
        {
            ConstantOperator => 1,
            EmptyOperator => 0,
            ContextItemOperator => 1,
            VariableOperator => 1,
            DocumentRootOperator => 1,
            AxisNavigationOperator nav => EstimateCardinality(nav.Input) * 10,
            FilterOperator filter => EstimateCardinality(filter.Input) / 2,
            FlworOperator => 100,
            FunctionCallOperator => 1,
            SequenceOperator seq => seq.Items.Sum(EstimateCardinality),
            _ => 1
        };
    }

    /// <summary>
    /// Determines whether a predicate expression references position() or last(),
    /// Per XPath spec: path expressions (/) must return results in document order.
    /// Simple map expressions (!) preserve evaluation order.
    /// </summary>
    private PhysicalOperator CreateSimpleMapPlan(SimpleMapExpression simpleMap, OptimizationContext context)
    {
        PhysicalOperator plan = new SimpleMapOperator
        {
            Left = CreatePhysicalPlan(simpleMap.Left, context),
            Right = CreatePhysicalPlan(simpleMap.Right, context),
            RequiresPositionalAccess = PredicateUsesPositionalAccess(simpleMap.Right),
            IsPathStep = simpleMap.IsPathStep
        };

        // Path steps (/) require document-order sorting; simple map (!) does not
        if (simpleMap.IsPathStep)
            plan = new DocumentOrderSortOperator { Input = plan };

        return plan;
    }

    /// <summary>
    /// Detects whether a predicate expression uses position()/last(),
    /// requiring full input materialization. Conservative: returns true if uncertain.
    /// </summary>
    private static bool PredicateUsesPositionalAccess(XQueryExpression predicate)
    {
        return predicate switch
        {
            // Numeric literal predicates like [1] or [3] are positional
            IntegerLiteral => true,
            // fn:position() or fn:last() calls are positional
            FunctionCallExpression fc when fc.Name.LocalName is "position" or "last" => true,
            // Boolean/string literals are not positional
            BooleanLiteral => false,
            StringLiteral => false,
            // For other expression types, conservatively assume positional access needed
            _ => true
        };
    }
}

/// <summary>
/// Context for query optimization.
/// </summary>
public sealed class OptimizationContext
{
    public required ContainerId Container { get; init; }
    public Dictionary<string, object>? Hints { get; init; }
    /// <summary>
    /// When true, arithmetic constant folding produces doubles instead of integers (XPath 1.0 BC mode).
    /// </summary>
    public bool BackwardsCompatible { get; init; }
    /// <summary>
    /// Function library for resolving keyword arguments to positional parameters.
    /// </summary>
    public Functions.FunctionLibrary? FunctionLibrary { get; init; }
    public Analysis.StaticContext? StaticContext { get; init; }
    /// <summary>
    /// When true, boundary whitespace in direct element constructors is preserved.
    /// Default false = strip (XQuery default).
    /// </summary>
    public bool BoundarySpacePreserve { get; set; }
}
