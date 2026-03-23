using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Parser.Grammar;
using XQueryParserType = PhoenixmlDb.XQuery.Parser.Grammar.XQueryParser;

namespace PhoenixmlDb.XQuery.Parser;

/// <summary>
/// Converts an ANTLR parse tree into the XQuery AST node types.
/// </summary>
internal sealed class XQueryAstBuilder : XQueryParserBaseVisitor<XQueryExpression>
{
    private static SourceLocation GetLocation(ParserRuleContext ctx)
    {
        var start = ctx.Start;
        var stop = ctx.Stop ?? start;
        return new SourceLocation(start.Line, start.Column, start.StartIndex, stop.StopIndex);
    }

    private static SourceLocation GetLocation(IToken token)
    {
        return new SourceLocation(token.Line, token.Column, token.StartIndex, token.StopIndex);
    }

    private static string UnquoteString(string literal)
    {
        // Remove surrounding quotes and unescape doubled quotes
        if (literal.Length < 2) return literal;
        var quote = literal[0];
        var inner = literal[1..^1];
        return quote == '"'
            ? inner.Replace("\"\"", "\"")
            : inner.Replace("''", "'");
    }

    private static QName MakeQName(string localName, string? prefix = null)
    {
        return new QName(default, localName, prefix);
    }

    // ==================== Module / Top-level ====================

    public override XQueryExpression VisitModule(XQueryParserType.ModuleContext context)
    {
        // For main modules, visit the query body
        var mainModule = context.mainModule();
        if (mainModule != null)
            return Visit(mainModule);

        // Library modules don't have a query body — return empty
        return EmptySequence.Instance;
    }

    public override XQueryExpression VisitMainModule(XQueryParserType.MainModuleContext context)
    {
        var prolog = context.prolog();
        if (prolog == null || prolog.ChildCount == 0)
            return Visit(context.queryBody());

        var declarations = new List<XQueryExpression>();

        // Process namespace declarations
        foreach (var nsDecl in prolog.namespaceDecl())
        {
            var prefix = GetNcNameText(nsDecl.ncName());
            var uri = UnquoteString(nsDecl.StringLiteral().GetText());
            declarations.Add(new NamespaceDeclarationExpression
            {
                Prefix = prefix,
                Uri = uri,
                Location = GetLocation(nsDecl)
            });
        }

        // Process module imports
        foreach (var importDecl in prolog.importDecl())
        {
            var moduleImport = importDecl.moduleImport();
            if (moduleImport != null)
            {
                string? prefix = null;
                if (moduleImport.ncName() != null)
                    prefix = GetNcNameText(moduleImport.ncName());

                var stringLiterals = moduleImport.StringLiteral();
                var namespaceUri = UnquoteString(stringLiterals[0].GetText());

                var locationHints = new List<string>();
                if (moduleImport.KW_AT() != null)
                {
                    for (var i = 1; i < stringLiterals.Length; i++)
                        locationHints.Add(UnquoteString(stringLiterals[i].GetText()));
                }

                declarations.Add(new ModuleImportExpression
                {
                    Prefix = prefix,
                    NamespaceUri = namespaceUri,
                    LocationHints = locationHints,
                    Location = GetLocation(importDecl)
                });
            }
        }

        // Process default namespace declarations
        foreach (var defNs in prolog.defaultNamespaceDecl())
        {
            var uri = UnquoteString(defNs.StringLiteral().GetText());
            var isFunction = defNs.KW_FUNCTION() != null;
            declarations.Add(new NamespaceDeclarationExpression
            {
                Prefix = isFunction ? "##default-function" : "##default-element",
                Uri = uri,
                Location = GetLocation(defNs)
            });
        }

        // Process variable declarations
        foreach (var varDecl in prolog.varDecl())
        {
            var varName = GetEqName(varDecl.varName().eqName());
            XdmSequenceType? typeDecl = null;
            if (varDecl.typeDeclaration() != null)
                typeDecl = BuildSequenceType(varDecl.typeDeclaration().sequenceType());

            var isExternal = varDecl.KW_EXTERNAL() != null;
            XQueryExpression? value = null;

            var exprSingle = varDecl.exprSingle();
            if (exprSingle != null)
                value = Visit(exprSingle);
            else if (!isExternal)
                value = EmptySequence.Instance;
            // else: external with no default — value stays null (resolved at runtime via binding)

            declarations.Add(new VariableDeclarationExpression
            {
                Name = varName,
                TypeDeclaration = typeDecl,
                Value = value,
                IsExternal = isExternal,
                Location = GetLocation(varDecl)
            });
        }

        // Process function declarations
        foreach (var funcDecl in prolog.functionDecl())
        {
            var funcName = GetEqName(funcDecl.eqName());
            var parameters = new List<FunctionParameter>();
            var paramList = funcDecl.paramList();
            if (paramList != null)
            {
                foreach (var p in paramList.param())
                {
                    var pName = GetEqName(p.varName().eqName());
                    XdmSequenceType? pType = null;
                    if (p.typeDeclaration() != null)
                        pType = BuildSequenceType(p.typeDeclaration().sequenceType());
                    parameters.Add(new FunctionParameter { Name = pName, Type = pType });
                }
            }

            XdmSequenceType? returnType = null;
            if (funcDecl.sequenceType() != null)
                returnType = BuildSequenceType(funcDecl.sequenceType());

            XQueryExpression body;
            if (funcDecl.KW_EXTERNAL() != null)
                body = EmptySequence.Instance;
            else
                body = Visit(funcDecl.enclosedExpr().expr()!);

            declarations.Add(new FunctionDeclarationExpression
            {
                Name = funcName,
                Parameters = parameters,
                ReturnType = returnType,
                Body = body,
                Location = GetLocation(funcDecl)
            });
        }

        if (declarations.Count == 0)
            return Visit(context.queryBody());

        return new ModuleExpression
        {
            Declarations = declarations,
            Body = Visit(context.queryBody()),
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitQueryBody(XQueryParserType.QueryBodyContext context)
    {
        return Visit(context.expr());
    }

    public override XQueryExpression VisitExpr(XQueryParserType.ExprContext context)
    {
        var singles = context.exprSingle();
        if (singles.Length == 1)
            return Visit(singles[0]);

        // Multiple comma-separated expressions → SequenceExpression
        var items = singles.Select(Visit).ToList();
        return new SequenceExpression
        {
            Items = items,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitExprSingle(XQueryParserType.ExprSingleContext context)
    {
        // ExprSingle delegates to one of the alternatives
        return Visit(context.children[0]);
    }

    // ==================== Literals ====================

    public override XQueryExpression VisitLiteral(XQueryParserType.LiteralContext context)
    {
        if (context.IntegerLiteral() != null)
        {
            var text = context.IntegerLiteral().GetText();
            object value;
            if (long.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var l))
                value = l;
            else
                value = System.Numerics.BigInteger.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            return new IntegerLiteral
            {
                Value = value,
                Location = GetLocation(context)
            };
        }
        if (context.DecimalLiteral() != null)
        {
            var text = context.DecimalLiteral().GetText();
            return new DecimalLiteral
            {
                Value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
                Location = GetLocation(context)
            };
        }
        if (context.DoubleLiteral() != null)
        {
            var text = context.DoubleLiteral().GetText();
            return new DoubleLiteral
            {
                Value = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
                Location = GetLocation(context)
            };
        }
        if (context.StringLiteral() != null)
        {
            return new StringLiteral
            {
                Value = UnquoteString(context.StringLiteral().GetText()),
                Location = GetLocation(context)
            };
        }
        throw new XQueryParseException($"Unknown literal type at {context.Start.Line}:{context.Start.Column}");
    }

    // ==================== Variables ====================

    public override XQueryExpression VisitVarRef(XQueryParserType.VarRefContext context)
    {
        var name = GetEqName(context.varName().eqName());
        return new VariableReference
        {
            Name = name,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitContextItemExpr(XQueryParserType.ContextItemExprContext context)
    {
        return ContextItemExpression.Instance;
    }

    public override XQueryExpression VisitParenthesizedExpr(XQueryParserType.ParenthesizedExprContext context)
    {
        var expr = context.expr();
        if (expr == null)
            return EmptySequence.Instance;
        return Visit(expr);
    }

    // ==================== Function Call ====================

    public override XQueryExpression VisitFunctionCall(XQueryParserType.FunctionCallContext context)
    {
        var name = GetEqName(context.eqName());
        var args = GetArguments(context.argumentList());

        // Per XQuery spec, reserved kind-test names with 0 args are node kind tests,
        // not function calls. The ANTLR grammar may parse them as function calls
        // when they appear in union expressions like "@*|node()".
        if (args.Count == 0 && name.Namespace == NamespaceId.None)
        {
            var kindTest = name.LocalName switch
            {
                "node" => new KindTest { Kind = XdmNodeKind.None },
                "text" => new KindTest { Kind = XdmNodeKind.Text },
                "comment" => new KindTest { Kind = XdmNodeKind.Comment },
                "processing-instruction" => new KindTest { Kind = XdmNodeKind.ProcessingInstruction },
                "document-node" => new KindTest { Kind = XdmNodeKind.Document },
                "element" => new KindTest { Kind = XdmNodeKind.Element },
                "attribute" => new KindTest { Kind = XdmNodeKind.Attribute },
                "namespace-node" => new KindTest { Kind = XdmNodeKind.Namespace },
                _ => (KindTest?)null
            };

            if (kindTest != null)
            {
                // Per XPath §3.3.2: attribute() and namespace-node() kind tests
                // default to the attribute and namespace axes respectively
                var axis = kindTest.Kind switch
                {
                    XdmNodeKind.Attribute => Axis.Attribute,
                    XdmNodeKind.Namespace => Axis.Namespace,
                    _ => Axis.Child
                };
                return new PathExpression
                {
                    IsAbsolute = false,
                    Steps = [new StepExpression
                    {
                        Axis = axis,
                        NodeTest = kindTest,
                        Predicates = [],
                        Location = GetLocation(context)
                    }],
                    Location = GetLocation(context)
                };
            }
        }

        return new FunctionCallExpression
        {
            Name = name,
            Arguments = args,
            Location = GetLocation(context)
        };
    }

    private List<XQueryExpression> GetArguments(XQueryParserType.ArgumentListContext argList)
    {
        var args = new List<XQueryExpression>();
        if (argList?.argument() == null) return args;

        foreach (var arg in argList.argument())
        {
            if (arg.QUESTION() != null)
                args.Add(ArgumentPlaceholder.Instance);
            else if (arg.ncName() != null && arg.ASSIGN() != null)
            {
                // XPath 4.0: keyword argument (name := value)
                args.Add(new KeywordArgument
                {
                    Name = GetNcNameText(arg.ncName()),
                    Value = Visit(arg.exprSingle()),
                    Location = GetLocation(arg)
                });
            }
            else
                args.Add(Visit(arg.exprSingle()));
        }
        return args;
    }

    public override XQueryExpression VisitNamedFunctionRef(XQueryParserType.NamedFunctionRefContext context)
    {
        var name = GetEqName(context.eqName());
        var arity = int.Parse(context.IntegerLiteral().GetText());
        return new NamedFunctionRef
        {
            Name = name,
            Arity = arity,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitInlineFunctionExpr(XQueryParserType.InlineFunctionExprContext context)
    {
        // Check for thin arrow shorthand: -> $x { body }
        if (context.THIN_ARROW() != null)
        {
            var varName = GetEqName(context.varName().eqName());
            var body = Visit(context.expr());
            return new InlineFunctionExpression
            {
                Parameters = [new FunctionParameter { Name = varName }],
                Body = body,
                Location = GetLocation(context)
            };
        }

        // Standard: function($params) as Type { body }
        var parameters = new List<FunctionParameter>();
        var paramList = context.paramList();
        if (paramList != null)
        {
            foreach (var p in paramList.param())
            {
                var pName = GetEqName(p.varName().eqName());
                XdmSequenceType? pType = null;
                if (p.typeDeclaration() != null)
                    pType = BuildSequenceType(p.typeDeclaration().sequenceType());

                parameters.Add(new FunctionParameter { Name = pName, Type = pType });
            }
        }

        XdmSequenceType? returnType = null;
        if (context.sequenceType() != null)
            returnType = BuildSequenceType(context.sequenceType());

        var funcBody = Visit(context.enclosedExpr().expr()!);
        return new InlineFunctionExpression
        {
            Parameters = parameters,
            ReturnType = returnType,
            Body = funcBody,
            Location = GetLocation(context)
        };
    }

    // ==================== Operator Expressions ====================

    public override XQueryExpression VisitOrExpr(XQueryParserType.OrExprContext context)
    {
        return BuildLeftAssociativeBinary(
            context.andExpr(),
            _ => BinaryOperator.Or,
            context);
    }

    public override XQueryExpression VisitAndExpr(XQueryParserType.AndExprContext context)
    {
        return BuildLeftAssociativeBinary(
            context.notExpr(),
            _ => BinaryOperator.And,
            context);
    }

    public override XQueryExpression VisitNotExpr(XQueryParserType.NotExprContext context)
    {
        var inner = Visit(context.comparisonExpr());
        if (context.KW_NOT() != null)
        {
            return new UnaryExpression
            {
                Operator = UnaryOperator.Not,
                Operand = inner,
                Location = GetLocation(context)
            };
        }
        return inner;
    }

    public override XQueryExpression VisitComparisonExpr(XQueryParserType.ComparisonExprContext context)
    {
        var operands = context.ftContainsExpr();
        if (operands.Length == 1)
            return Visit(operands[0]);

        var left = Visit(operands[0]);
        var right = Visit(operands[1]);
        var op = GetComparisonOperator(context.compOp());

        return new BinaryExpression
        {
            Left = left,
            Operator = op,
            Right = right,
            Location = GetLocation(context)
        };
    }

    private static BinaryOperator GetComparisonOperator(XQueryParserType.CompOpContext ctx)
    {
        if (ctx.KW_EQ() != null) return BinaryOperator.Equal;
        if (ctx.KW_NE() != null) return BinaryOperator.NotEqual;
        if (ctx.KW_LT() != null) return BinaryOperator.LessThan;
        if (ctx.KW_LE() != null) return BinaryOperator.LessOrEqual;
        if (ctx.KW_GT() != null) return BinaryOperator.GreaterThan;
        if (ctx.KW_GE() != null) return BinaryOperator.GreaterOrEqual;
        if (ctx.EQUALS() != null) return BinaryOperator.GeneralEqual;
        if (ctx.NOT_EQUALS() != null) return BinaryOperator.GeneralNotEqual;
        if (ctx.LESS_THAN() != null) return BinaryOperator.GeneralLessThan;
        if (ctx.LESS_EQ() != null) return BinaryOperator.GeneralLessOrEqual;
        if (ctx.GREATER_THAN() != null) return BinaryOperator.GeneralGreaterThan;
        if (ctx.GREATER_EQ() != null) return BinaryOperator.GeneralGreaterOrEqual;
        if (ctx.KW_IS() != null) return BinaryOperator.Is;
        if (ctx.LSHIFT() != null) return BinaryOperator.Precedes;
        if (ctx.RSHIFT() != null) return BinaryOperator.Follows;
        throw new XQueryParseException($"Unknown comparison operator at {ctx.Start.Line}:{ctx.Start.Column}");
    }

    public override XQueryExpression VisitOtherwiseExpr(XQueryParserType.OtherwiseExprContext context)
    {
        return BuildLeftAssociativeBinary(
            context.stringConcatExpr(),
            _ => BinaryOperator.Otherwise,
            context);
    }

    public override XQueryExpression VisitStringConcatExpr(XQueryParserType.StringConcatExprContext context)
    {
        var operands = context.rangeExpr();
        if (operands.Length == 1)
            return Visit(operands[0]);

        var items = operands.Select(Visit).ToList();
        return new StringConcatExpression
        {
            Operands = items,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitRangeExpr(XQueryParserType.RangeExprContext context)
    {
        var operands = context.additiveExpr();
        if (operands.Length == 1)
            return Visit(operands[0]);

        return new RangeExpression
        {
            Start = Visit(operands[0]),
            End = Visit(operands[1]),
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitAdditiveExpr(XQueryParserType.AdditiveExprContext context)
    {
        return BuildLeftAssociativeBinaryFromTokens(
            context.multiplicativeExpr(),
            context.children,
            token => token.Symbol.Type switch
            {
                XQueryLexer.PLUS => BinaryOperator.Add,
                XQueryLexer.MINUS => BinaryOperator.Subtract,
                _ => throw new XQueryParseException($"Unexpected additive operator")
            },
            context);
    }

    public override XQueryExpression VisitMultiplicativeExpr(XQueryParserType.MultiplicativeExprContext context)
    {
        return BuildLeftAssociativeBinaryFromTokens(
            context.unionExpr(),
            context.children,
            token => token.Symbol.Type switch
            {
                XQueryLexer.STAR => BinaryOperator.Multiply,
                XQueryLexer.KW_DIV => BinaryOperator.Divide,
                XQueryLexer.KW_IDIV => BinaryOperator.IntegerDivide,
                XQueryLexer.KW_MOD => BinaryOperator.Modulo,
                _ => throw new XQueryParseException($"Unexpected multiplicative operator")
            },
            context);
    }

    public override XQueryExpression VisitUnionExpr(XQueryParserType.UnionExprContext context)
    {
        return BuildLeftAssociativeBinary(
            context.intersectExceptExpr(),
            _ => BinaryOperator.Union,
            context);
    }

    public override XQueryExpression VisitIntersectExceptExpr(XQueryParserType.IntersectExceptExprContext context)
    {
        return BuildLeftAssociativeBinaryFromTokens(
            context.instanceofExpr(),
            context.children,
            token => token.Symbol.Type switch
            {
                XQueryLexer.KW_INTERSECT => BinaryOperator.Intersect,
                XQueryLexer.KW_EXCEPT => BinaryOperator.Except,
                _ => throw new XQueryParseException($"Unexpected set operator")
            },
            context);
    }

    public override XQueryExpression VisitInstanceofExpr(XQueryParserType.InstanceofExprContext context)
    {
        var expr = Visit(context.treatExpr());
        if (context.sequenceType() != null)
        {
            return new InstanceOfExpression
            {
                Expression = expr,
                TargetType = BuildSequenceType(context.sequenceType()),
                Location = GetLocation(context)
            };
        }
        return expr;
    }

    public override XQueryExpression VisitTreatExpr(XQueryParserType.TreatExprContext context)
    {
        var expr = Visit(context.castableExpr());
        if (context.sequenceType() != null)
        {
            return new TreatExpression
            {
                Expression = expr,
                TargetType = BuildSequenceType(context.sequenceType()),
                Location = GetLocation(context)
            };
        }
        return expr;
    }

    public override XQueryExpression VisitCastableExpr(XQueryParserType.CastableExprContext context)
    {
        var expr = Visit(context.castExpr());
        if (context.singleType() != null)
        {
            return new CastableExpression
            {
                Expression = expr,
                TargetType = BuildSingleType(context.singleType()),
                Location = GetLocation(context)
            };
        }
        return expr;
    }

    public override XQueryExpression VisitCastExpr(XQueryParserType.CastExprContext context)
    {
        var expr = Visit(context.arrowExpr());
        if (context.singleType() != null)
        {
            return new CastExpression
            {
                Expression = expr,
                TargetType = BuildSingleType(context.singleType()),
                Location = GetLocation(context)
            };
        }
        return expr;
    }

    public override XQueryExpression VisitArrowExpr(XQueryParserType.ArrowExprContext context)
    {
        var expr = Visit(context.unaryExpr());
        var arrowOps = context.arrowOp();
        var funcSpecs = context.arrowFunctionSpecifier();
        var argLists = context.argumentList();

        for (int i = 0; i < arrowOps.Length; i++)
        {
            var funcSpec = funcSpecs[i];
            var argList = argLists[i];
            var args = GetArguments(argList);
            var isThinArrow = arrowOps[i].THIN_ARROW() != null;

            // Fat arrow (=>): expr => fn(args) → fn(expr, args) — expr is first argument
            // Thin arrow (->): expr -> fn(args) → fn(args) with expr as context item
            if (funcSpec.eqName() != null)
            {
                var funcName = GetEqName(funcSpec.eqName());
                var allArgs = isThinArrow ? new List<XQueryExpression>(args) : new List<XQueryExpression> { expr };
                if (!isThinArrow) allArgs.AddRange(args);

                var call = new FunctionCallExpression
                {
                    Name = funcName,
                    Arguments = allArgs,
                    Location = GetLocation(context)
                };

                expr = new ArrowExpression
                {
                    Expression = expr,
                    FunctionCall = call,
                    IsThinArrow = isThinArrow,
                    Location = GetLocation(context)
                };
            }
            else
            {
                // Variable or parenthesized — dynamic function call
                var funcExpr = Visit(funcSpec);
                var allArgs = isThinArrow ? new List<XQueryExpression>(args) : new List<XQueryExpression> { expr };
                if (!isThinArrow) allArgs.AddRange(args);

                expr = new ArrowExpression
                {
                    Expression = expr,
                    FunctionCall = new DynamicFunctionCallExpression
                    {
                        FunctionExpression = funcExpr,
                        Arguments = allArgs,
                        Location = GetLocation(context)
                    },
                    IsThinArrow = isThinArrow,
                    Location = GetLocation(context)
                };
            }
        }

        return expr;
    }

    public override XQueryExpression VisitUnaryExpr(XQueryParserType.UnaryExprContext context)
    {
        var expr = Visit(context.simpleMapExpr());

        // Count unary operators from right-to-left
        var ops = new List<UnaryOperator>();
        foreach (var child in context.children)
        {
            if (child is ITerminalNode tn)
            {
                if (tn.Symbol.Type == XQueryLexer.MINUS)
                    ops.Add(UnaryOperator.Minus);
                else if (tn.Symbol.Type == XQueryLexer.PLUS)
                    ops.Add(UnaryOperator.Plus);
            }
        }

        // Apply operators from innermost to outermost (right to left in source)
        for (int i = ops.Count - 1; i >= 0; i--)
        {
            expr = new UnaryExpression
            {
                Operator = ops[i],
                Operand = expr,
                Location = GetLocation(context)
            };
        }

        return expr;
    }

    public override XQueryExpression VisitSimpleMapExpr(XQueryParserType.SimpleMapExprContext context)
    {
        var paths = context.pathExpr();
        if (paths.Length == 1)
            return Visit(paths[0]);

        var result = Visit(paths[0]);
        for (int i = 1; i < paths.Length; i++)
        {
            result = new SimpleMapExpression
            {
                Left = result,
                Right = Visit(paths[i]),
                Location = GetLocation(context)
            };
        }
        return result;
    }

    // ==================== Path Expressions ====================

    public override XQueryExpression VisitRootedPath(XQueryParserType.RootedPathContext context)
    {
        var relative = context.relativePathExpr();
        if (relative == null)
        {
            // Just "/" — root document
            return new PathExpression
            {
                IsAbsolute = true,
                Steps = [],
                Location = GetLocation(context)
            };
        }

        return BuildRelativePathExpression(relative, isAbsolute: true, location: GetLocation(context));
    }

    public override XQueryExpression VisitDescendantPath(XQueryParserType.DescendantPathContext context)
    {
        return BuildRelativePathExpression(context.relativePathExpr(), isAbsolute: true, location: GetLocation(context),
            prependDescendantOrSelf: true);
    }

    public override XQueryExpression VisitRelativePath(XQueryParserType.RelativePathContext context)
    {
        var stepContexts = context.relativePathExpr().stepExpr();

        // If there's exactly one step and it's a postfix expression (not an axis step),
        // delegate directly to the postfix expression to produce a non-path AST node
        if (stepContexts.Length == 1 && stepContexts[0].postfixExpr() != null)
        {
            return Visit(stepContexts[0].postfixExpr());
        }

        var relativePathCtx = context.relativePathExpr();
        return BuildRelativePathExpression(relativePathCtx, isAbsolute: false, location: GetLocation(context));
    }

    /// <summary>
    /// Builds a path expression from a relative path, properly handling postfix expression
    /// steps (parenthesized expressions, function calls, etc.) at any position.
    /// When a postfix step appears in the middle of a path (e.g., A/(expr)/B), it is
    /// converted to a SimpleMapExpression mapping operation.
    /// </summary>
    private XQueryExpression BuildRelativePathExpression(
        XQueryParserType.RelativePathExprContext ctx,
        bool isAbsolute,
        SourceLocation? location,
        bool prependDescendantOrSelf = false)
    {
        // Collect all steps with their separator info (/ or //)
        var parsedSteps = new List<(bool isAxis, StepExpression? axisStep, XQueryExpression? postfixExpr, bool precededByDSlash)>();

        int stepIdx = 0;
        bool nextPrecededByDSlash = prependDescendantOrSelf;
        var stepContexts = ctx.stepExpr();

        for (int i = 0; i < ctx.ChildCount; i++)
        {
            var child = ctx.children[i];
            if (child is ITerminalNode tn && tn.Symbol.Type == XQueryLexer.DSLASH)
            {
                nextPrecededByDSlash = true;
            }
            else if (child is XQueryParserType.StepExprContext stepCtx)
            {
                if (stepCtx.axisStep() != null)
                {
                    var step = BuildAxisStep(stepCtx.axisStep());
                    parsedSteps.Add((true, step, null, nextPrecededByDSlash));
                }
                else
                {
                    // PostfixExpr step
                    var expr = Visit(stepCtx.postfixExpr());
                    parsedSteps.Add((false, null, expr, nextPrecededByDSlash));
                }
                nextPrecededByDSlash = false;
                stepIdx++;
            }
        }

        if (parsedSteps.Count == 0)
        {
            return new PathExpression
            {
                IsAbsolute = isAbsolute,
                Steps = [],
                Location = location
            };
        }

        // Build the expression by processing steps left to right.
        // We accumulate axis steps into PathExpressions and insert SimpleMapExpressions
        // when we encounter postfix expression steps.
        XQueryExpression? current = null;
        var currentSteps = new List<StepExpression>();
        bool currentIsAbsolute = isAbsolute;

        for (int i = 0; i < parsedSteps.Count; i++)
        {
            var (isAxisStep, axisStep, postfixExpr, precededByDSlash) = parsedSteps[i];

            if (precededByDSlash)
            {
                currentSteps.Add(new StepExpression
                {
                    Axis = Axis.DescendantOrSelf,
                    NodeTest = new KindTest { Kind = XdmNodeKind.None },
                    Predicates = [],
                    Location = location
                });
            }

            if (isAxisStep)
            {
                currentSteps.Add(axisStep!);
            }
            else
            {
                // PostfixExpr step — flush accumulated axis steps to a path, then map
                if (i == 0 && !currentIsAbsolute && currentSteps.Count == 0)
                {
                    // First step is a postfixExpr (e.g., $var/path) — use as initial expression
                    current = postfixExpr;
                }
                else
                {
                    // Flush accumulated axis steps into a path
                    if (currentSteps.Count > 0 || currentIsAbsolute || current == null)
                    {
                        var path = new PathExpression
                        {
                            IsAbsolute = currentIsAbsolute,
                            InitialExpression = current,
                            Steps = currentSteps,
                            Location = location
                        };
                        current = path;
                        currentSteps = [];
                        currentIsAbsolute = false;
                    }

                    // Map the postfix expression over each node from the left side
                    current = new SimpleMapExpression
                    {
                        Left = current,
                        Right = postfixExpr!,
                        IsPathStep = true,
                        Location = location
                    };
                }
                currentIsAbsolute = false;
            }
        }

        // Flush any remaining axis steps
        if (currentSteps.Count > 0 || current == null)
        {
            return new PathExpression
            {
                IsAbsolute = currentIsAbsolute,
                InitialExpression = current,
                Steps = currentSteps,
                Location = location
            };
        }

        return current;
    }

    private StepExpression BuildAxisStep(XQueryParserType.AxisStepContext ctx)
    {
        Axis axis;
        Ast.NodeTest nodeTest;

        var forwardStep = ctx.forwardStep();
        var reverseStep = ctx.reverseStep();

        if (forwardStep != null)
        {
            if (forwardStep.forwardAxis() != null)
            {
                axis = GetForwardAxis(forwardStep.forwardAxis());
                nodeTest = BuildNodeTest(forwardStep.nodeTest());
            }
            else
            {
                // Abbreviated forward step
                var abbrev = forwardStep.abbrevForwardStep();
                nodeTest = BuildNodeTest(abbrev.nodeTest());
                // Per XPath §3.3.2: if there's an @, or if the node test is attribute()
                // or namespace-node(), use the corresponding axis
                if (abbrev.AT_SIGN() != null)
                    axis = Axis.Attribute;
                else if (nodeTest is KindTest kt && kt.Kind == XdmNodeKind.Attribute)
                    axis = Axis.Attribute;
                else if (nodeTest is KindTest kt2 && kt2.Kind == XdmNodeKind.Namespace)
                    axis = Axis.Namespace;
                else
                    axis = Axis.Child;
            }
        }
        else
        {
            if (reverseStep!.reverseAxis() != null)
            {
                axis = GetReverseAxis(reverseStep.reverseAxis());
                nodeTest = BuildNodeTest(reverseStep.nodeTest());
            }
            else
            {
                // Abbreviated reverse step: ..
                axis = Axis.Parent;
                nodeTest = new KindTest { Kind = XdmNodeKind.None };
            }
        }

        var predicates = BuildPredicates(ctx.predicateList());

        return new StepExpression
        {
            Axis = axis,
            NodeTest = nodeTest,
            Predicates = predicates,
            Location = GetLocation(ctx)
        };
    }

    private static Axis GetForwardAxis(XQueryParserType.ForwardAxisContext ctx)
    {
        if (ctx.KW_CHILD() != null) return Axis.Child;
        if (ctx.KW_DESCENDANT() != null) return Axis.Descendant;
        if (ctx.KW_ATTRIBUTE() != null) return Axis.Attribute;
        if (ctx.KW_SELF() != null) return Axis.Self;
        if (ctx.KW_DESCENDANT_OR_SELF() != null) return Axis.DescendantOrSelf;
        if (ctx.KW_FOLLOWING_SIBLING() != null) return Axis.FollowingSibling;
        if (ctx.KW_FOLLOWING() != null) return Axis.Following;
        if (ctx.KW_NAMESPACE() != null) return Axis.Namespace;
        throw new XQueryParseException($"Unknown forward axis");
    }

    private static Axis GetReverseAxis(XQueryParserType.ReverseAxisContext ctx)
    {
        if (ctx.KW_PARENT() != null) return Axis.Parent;
        if (ctx.KW_ANCESTOR() != null) return Axis.Ancestor;
        if (ctx.KW_PRECEDING_SIBLING() != null) return Axis.PrecedingSibling;
        if (ctx.KW_PRECEDING() != null) return Axis.Preceding;
        if (ctx.KW_ANCESTOR_OR_SELF() != null) return Axis.AncestorOrSelf;
        throw new XQueryParseException($"Unknown reverse axis");
    }

    private Ast.NodeTest BuildNodeTest(XQueryParserType.NodeTestContext ctx)
    {
        if (ctx.kindTest() != null)
            return BuildKindTest(ctx.kindTest());
        return BuildNameTest(ctx.nameTest());
    }

    private Ast.NodeTest BuildNameTest(XQueryParserType.NameTestContext ctx)
    {
        if (ctx.eqName() != null)
        {
            var name = GetEqName(ctx.eqName());
            return new NameTest
            {
                LocalName = name.LocalName,
                Prefix = name.Prefix
            };
        }
        // Wildcard
        return Visit(ctx.wildcard()) switch
        {
            _ => BuildWildcardTest(ctx.wildcard())
        };
    }

    private static Ast.NodeTest BuildWildcardTest(XQueryParserType.WildcardContext ctx)
    {
        if (ctx is XQueryParserType.WildcardAllContext)
            return new NameTest { LocalName = "*" };
        if (ctx is XQueryParserType.WildcardLocalAllContext wla)
            return new NameTest { LocalName = "*", Prefix = GetNcNameText(wla.ncName()) };
        if (ctx is XQueryParserType.WildcardNsAllContext wna)
            return new NameTest { LocalName = GetNcNameText(wna.ncName()), NamespaceUri = "*" };
        if (ctx is XQueryParserType.WildcardBracedUriAllContext wbu)
        {
            // Q{namespace-uri}* — wildcard matching all names in the given namespace
            var text = wbu.BracedURILiteral().GetText(); // e.g. "Q{http://example.com}"
            var namespaceUri = text[2..^1]; // Extract content between Q{ and }
            var nt = new NameTest { LocalName = "*", NamespaceUri = namespaceUri };
            // Pre-resolve empty namespace to NamespaceId.None
            if (string.IsNullOrEmpty(namespaceUri))
                nt.ResolvedNamespace = NamespaceId.None;
            return nt;
        }
        throw new XQueryParseException("Unknown wildcard type");
    }

    private Ast.NodeTest BuildKindTest(XQueryParserType.KindTestContext ctx)
    {
        if (ctx.anyKindTest() != null)
            return new KindTest { Kind = XdmNodeKind.None };
        if (ctx.textTest() != null)
            return new KindTest { Kind = XdmNodeKind.Text };
        if (ctx.commentTest() != null)
            return new KindTest { Kind = XdmNodeKind.Comment };
        if (ctx.namespaceNodeTest() != null)
            return new KindTest { Kind = XdmNodeKind.Namespace };
        if (ctx.piTest() != null)
        {
            var pi = ctx.piTest();
            NameTest? name = null;
            if (pi.ncName() != null)
                name = new NameTest { LocalName = GetNcNameText(pi.ncName()) };
            else if (pi.StringLiteral() != null)
                name = new NameTest { LocalName = UnquoteString(pi.StringLiteral().GetText()) };
            return new KindTest { Kind = XdmNodeKind.ProcessingInstruction, Name = name };
        }
        if (ctx.documentTest() != null)
            return new KindTest { Kind = XdmNodeKind.Document };
        if (ctx.elementTest() != null)
        {
            var et = ctx.elementTest();
            NameTest? name = null;
            XdmTypeName? typeName = null;
            var isWildcard = et.STAR() != null;
            if (!isWildcard && et.eqName().Length > 0)
                name = new NameTest { LocalName = GetEqName(et.eqName()[0]).LocalName, Prefix = GetEqName(et.eqName()[0]).Prefix };
            // Type annotation is the eqName after COMMA (first eqName if wildcard, second if named)
            var typeIdx = isWildcard ? 0 : 1;
            if (et.eqName().Length > typeIdx)
            {
                var tn = GetEqName(et.eqName()[typeIdx]);
                typeName = new XdmTypeName { LocalName = tn.LocalName, Prefix = tn.Prefix };
            }
            return new KindTest { Kind = XdmNodeKind.Element, Name = name, TypeName = typeName };
        }
        if (ctx.attributeTest() != null)
        {
            var at = ctx.attributeTest();
            NameTest? name = null;
            XdmTypeName? typeName = null;
            var isWildcard = at.STAR() != null;
            if (!isWildcard && at.eqName().Length > 0)
                name = new NameTest { LocalName = GetEqName(at.eqName()[0]).LocalName, Prefix = GetEqName(at.eqName()[0]).Prefix };
            var typeIdx = isWildcard ? 0 : 1;
            if (at.eqName().Length > typeIdx)
            {
                var tn = GetEqName(at.eqName()[typeIdx]);
                typeName = new XdmTypeName { LocalName = tn.LocalName, Prefix = tn.Prefix };
            }
            return new KindTest { Kind = XdmNodeKind.Attribute, Name = name, TypeName = typeName };
        }
        // Schema tests
        return new KindTest { Kind = XdmNodeKind.None };
    }

    private List<XQueryExpression> BuildPredicates(XQueryParserType.PredicateListContext ctx)
    {
        var preds = new List<XQueryExpression>();
        if (ctx?.predicate() == null) return preds;

        foreach (var pred in ctx.predicate())
        {
            preds.Add(Visit(pred.expr()));
        }
        return preds;
    }

    // ==================== Postfix Expressions ====================

    public override XQueryExpression VisitPostfixExpr(XQueryParserType.PostfixExprContext context)
    {
        var expr = Visit(context.primaryExpr());

        // Apply predicates, argument lists, and lookups
        foreach (var child in context.children.Skip(1))
        {
            if (child is XQueryParserType.PredicateContext pred)
            {
                expr = new FilterExpression
                {
                    Primary = expr,
                    Predicates = [Visit(pred.expr())],
                    Location = GetLocation(pred)
                };
            }
            else if (child is XQueryParserType.ArgumentListContext argList)
            {
                var args = GetArguments(argList);
                expr = new DynamicFunctionCallExpression
                {
                    FunctionExpression = expr,
                    Arguments = args,
                    Location = GetLocation(argList)
                };
            }
            else if (child is XQueryParserType.LookupContext lookup)
            {
                var key = BuildLookupKey(lookup.keySpecifier());
                expr = new LookupExpression
                {
                    Base = expr,
                    Key = key,
                    Location = GetLocation(lookup)
                };
            }
        }

        return expr;
    }

    private XQueryExpression? BuildLookupKey(XQueryParserType.KeySpecifierContext ctx)
    {
        if (ctx.STAR() != null) return null; // Wildcard lookup
        if (ctx.ncName() != null)
            return new StringLiteral { Value = GetNcNameText(ctx.ncName()) };
        if (ctx.IntegerLiteral() != null)
            return new IntegerLiteral { Value = long.Parse(ctx.IntegerLiteral().GetText()) };
        if (ctx.parenthesizedExpr() != null)
            return Visit(ctx.parenthesizedExpr());
        throw new XQueryParseException("Unknown key specifier");
    }

    public override XQueryExpression VisitUnaryLookup(XQueryParserType.UnaryLookupContext context)
    {
        var key = BuildLookupKey(context.keySpecifier());
        return new UnaryLookupExpression
        {
            Key = key,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitPrimaryExpr(XQueryParserType.PrimaryExprContext context)
    {
        // Delegate to the single child
        return Visit(context.children[0]);
    }

    // ==================== FLWOR ====================

    public override XQueryExpression VisitFlworExpr(XQueryParserType.FlworExprContext context)
    {
        var clauses = new List<FlworClause>();

        // Initial clause
        clauses.Add(BuildFlworClause(context.initialClause()));

        // Intermediate clauses
        foreach (var intermediate in context.intermediateClause())
        {
            clauses.Add(BuildFlworClause(intermediate));
        }

        var exprSingles = context.exprSingle();
        var returnExpr = Visit(exprSingles[0]);

        // XPath 4.0: optional otherwise clause
        XQueryExpression? otherwiseExpr = null;
        if (exprSingles.Length > 1)
            otherwiseExpr = Visit(exprSingles[1]);

        return new FlworExpression
        {
            Clauses = clauses,
            ReturnExpression = returnExpr,
            OtherwiseExpression = otherwiseExpr,
            Location = GetLocation(context)
        };
    }

    private FlworClause BuildFlworClause(XQueryParserType.InitialClauseContext ctx)
    {
        if (ctx.forClause() != null) return BuildForClause(ctx.forClause());
        if (ctx.letClause() != null) return BuildLetClause(ctx.letClause());
        if (ctx.windowClause() != null) return BuildWindowClause(ctx.windowClause());
        throw new XQueryParseException("Unknown initial clause type");
    }

    private FlworClause BuildFlworClause(XQueryParserType.IntermediateClauseContext ctx)
    {
        if (ctx.forClause() != null) return BuildForClause(ctx.forClause());
        if (ctx.letClause() != null) return BuildLetClause(ctx.letClause());
        if (ctx.whereClause() != null) return new WhereClause { Condition = Visit(ctx.whereClause().exprSingle()) };
        if (ctx.orderByClause() != null) return BuildOrderByClause(ctx.orderByClause());
        if (ctx.groupByClause() != null) return BuildGroupByClause(ctx.groupByClause());
        if (ctx.countClause() != null) return new CountClause { Variable = GetEqName(ctx.countClause().varName().eqName()) };
        if (ctx.windowClause() != null) return BuildWindowClause(ctx.windowClause());
        if (ctx.whileClause() != null) return new Ast.WhileClause { Condition = Visit(ctx.whileClause().exprSingle()) };
        throw new XQueryParseException("Unknown intermediate clause type");
    }

    private ForClause BuildForClause(XQueryParserType.ForClauseContext ctx)
    {
        var bindings = ctx.forBinding().Select(BuildForBinding).ToList();
        var isMember = ctx.KW_MEMBER() != null;
        return new ForClause { Bindings = bindings, IsMember = isMember };
    }

    private ForBinding BuildForBinding(XQueryParserType.ForBindingContext ctx)
    {
        var varName = GetEqName(ctx.varName().eqName());
        XdmSequenceType? typeDecl = null;
        if (ctx.typeDeclaration() != null)
            typeDecl = BuildSequenceType(ctx.typeDeclaration().sequenceType());

        QName? posVar = null;
        if (ctx.positionalVar() != null)
            posVar = GetEqName(ctx.positionalVar().varName().eqName());

        return new ForBinding
        {
            Variable = varName,
            TypeDeclaration = typeDecl,
            AllowingEmpty = ctx.allowingEmpty() != null,
            PositionalVariable = posVar,
            Expression = Visit(ctx.exprSingle())
        };
    }

    private LetClause BuildLetClause(XQueryParserType.LetClauseContext ctx)
    {
        var bindings = ctx.letBinding().Select(BuildLetBinding).ToList();
        return new LetClause { Bindings = bindings };
    }

    private LetBinding BuildLetBinding(XQueryParserType.LetBindingContext ctx)
    {
        var varName = GetEqName(ctx.varName().eqName());
        XdmSequenceType? typeDecl = null;
        if (ctx.typeDeclaration() != null)
            typeDecl = BuildSequenceType(ctx.typeDeclaration().sequenceType());

        return new LetBinding
        {
            Variable = varName,
            TypeDeclaration = typeDecl,
            Expression = Visit(ctx.exprSingle())
        };
    }

    private OrderByClause BuildOrderByClause(XQueryParserType.OrderByClauseContext ctx)
    {
        bool stable = ctx.KW_STABLE() != null;
        var specs = ctx.orderSpecList().orderSpec().Select(BuildOrderSpec).ToList();
        return new OrderByClause { Stable = stable, OrderSpecs = specs };
    }

    private OrderSpec BuildOrderSpec(XQueryParserType.OrderSpecContext ctx)
    {
        var direction = OrderDirection.Ascending;
        if (ctx.orderDirection() != null)
            direction = ctx.orderDirection().KW_DESCENDING() != null ? OrderDirection.Descending : OrderDirection.Ascending;

        var emptyOrder = EmptyOrder.Least;
        if (ctx.emptyOrderSpec() != null)
            emptyOrder = ctx.emptyOrderSpec().KW_GREATEST() != null ? EmptyOrder.Greatest : EmptyOrder.Least;

        string? collation = null;
        if (ctx.collationSpec() != null)
            collation = UnquoteString(ctx.collationSpec().StringLiteral().GetText());

        return new OrderSpec
        {
            Expression = Visit(ctx.exprSingle()),
            Direction = direction,
            EmptyOrder = emptyOrder,
            Collation = collation
        };
    }

    private GroupByClause BuildGroupByClause(XQueryParserType.GroupByClauseContext ctx)
    {
        var specs = ctx.groupingSpecList().groupingSpec().Select(BuildGroupingSpec).ToList();
        return new GroupByClause { GroupingSpecs = specs };
    }

    private GroupingSpec BuildGroupingSpec(XQueryParserType.GroupingSpecContext ctx)
    {
        var varName = GetEqName(ctx.varName().eqName());
        XQueryExpression? expr = null;
        if (ctx.exprSingle() != null)
            expr = Visit(ctx.exprSingle());

        string? collation = null;
        if (ctx.collationSpec() != null)
            collation = UnquoteString(ctx.collationSpec().StringLiteral().GetText());

        return new GroupingSpec { Variable = varName, Expression = expr, Collation = collation };
    }

    private WindowClause BuildWindowClause(XQueryParserType.WindowClauseContext ctx)
    {
        var kind = ctx.KW_TUMBLING() != null ? WindowKind.Tumbling : WindowKind.Sliding;
        var varName = GetEqName(ctx.varName().eqName());
        XdmSequenceType? typeDecl = null;
        if (ctx.typeDeclaration() != null)
            typeDecl = BuildSequenceType(ctx.typeDeclaration().sequenceType());

        var inExpr = Visit(ctx.exprSingle());
        var startCond = BuildWindowCondition(ctx.windowStartCondition());
        WindowCondition? endCond = null;
        if (ctx.windowEndCondition() != null)
            endCond = BuildWindowCondition(ctx.windowEndCondition());

        return new WindowClause
        {
            Kind = kind,
            Variable = varName,
            TypeDeclaration = typeDecl,
            Expression = inExpr,
            Start = startCond,
            End = endCond
        };
    }

    private WindowCondition BuildWindowCondition(XQueryParserType.WindowStartConditionContext ctx)
    {
        var vars = ctx.windowVars();
        return new WindowCondition
        {
            CurrentItem = GetWindowVar(vars),
            Position = GetWindowPositionalVar(vars),
            PreviousItem = GetWindowPreviousVar(vars),
            NextItem = GetWindowNextVar(vars),
            When = Visit(ctx.exprSingle())
        };
    }

    private WindowCondition BuildWindowCondition(XQueryParserType.WindowEndConditionContext ctx)
    {
        var vars = ctx.windowVars();
        return new WindowCondition
        {
            CurrentItem = GetWindowVar(vars),
            Position = GetWindowPositionalVar(vars),
            PreviousItem = GetWindowPreviousVar(vars),
            NextItem = GetWindowNextVar(vars),
            When = Visit(ctx.exprSingle())
        };
    }

    private static QName? GetWindowVar(XQueryParserType.WindowVarsContext ctx)
    {
        // First $ varName in windowVars (the current item var)
        var varName = ctx.varName();
        return varName != null ? GetEqName(varName.eqName()) : null;
    }

    private static QName? GetWindowPositionalVar(XQueryParserType.WindowVarsContext ctx)
    {
        var pv = ctx.positionalVarInWindow();
        return pv != null ? GetEqName(pv.varName().eqName()) : null;
    }

    private static QName? GetWindowPreviousVar(XQueryParserType.WindowVarsContext ctx)
    {
        var pv = ctx.previousVarInWindow();
        return pv != null ? GetEqName(pv.varName().eqName()) : null;
    }

    private static QName? GetWindowNextVar(XQueryParserType.WindowVarsContext ctx)
    {
        var nv = ctx.nextVarInWindow();
        return nv != null ? GetEqName(nv.varName().eqName()) : null;
    }

    // ==================== Conditionals ====================

    public override XQueryExpression VisitIfThenElse(XQueryParserType.IfThenElseContext context)
    {
        var condition = Visit(context.expr());
        var then = Visit(context.exprSingle()[0]);
        var @else = Visit(context.exprSingle()[1]);

        return new IfExpression
        {
            Condition = condition,
            Then = then,
            Else = @else,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitBracedIf(XQueryParserType.BracedIfContext context)
    {
        // Braced if: if (cond) { body } — no else
        var exprs = context.expr();
        var condition = Visit(exprs[0]);
        var body = Visit(exprs[1]);

        return new IfExpression
        {
            Condition = condition,
            Then = body,
            Else = null,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitQuantifiedExpr(XQueryParserType.QuantifiedExprContext context)
    {
        var quantifier = context.KW_SOME() != null ? Quantifier.Some : Quantifier.Every;
        var bindings = context.quantifiedBinding().Select(b =>
        {
            var varName = GetEqName(b.varName().eqName());
            XdmSequenceType? typeDecl = null;
            if (b.typeDeclaration() != null)
                typeDecl = BuildSequenceType(b.typeDeclaration().sequenceType());

            return new QuantifiedBinding
            {
                Variable = varName,
                TypeDeclaration = typeDecl,
                Expression = Visit(b.exprSingle())
            };
        }).ToList();

        return new QuantifiedExpression
        {
            Quantifier = quantifier,
            Bindings = bindings,
            Satisfies = Visit(context.exprSingle()),
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitSwitchExpr(XQueryParserType.SwitchExprContext context)
    {
        var operand = Visit(context.expr());
        var cases = context.switchCaseClause().Select(BuildSwitchCase).ToList();
        var defaultExpr = Visit(context.exprSingle());

        return new SwitchExpression
        {
            Operand = operand,
            Cases = cases,
            Default = defaultExpr,
            Location = GetLocation(context)
        };
    }

    private SwitchCase BuildSwitchCase(XQueryParserType.SwitchCaseClauseContext ctx)
    {
        var values = ctx.switchCaseOperand().Select(op => Visit(op.exprSingle())).ToList();
        return new SwitchCase
        {
            Values = values,
            Result = Visit(ctx.exprSingle())
        };
    }

    public override XQueryExpression VisitTypeswitchExpr(XQueryParserType.TypeswitchExprContext context)
    {
        var operand = Visit(context.expr());
        var cases = context.typeswitchCaseClause().Select(BuildTypeswitchCase).ToList();

        QName? defaultVar = null;
        if (context.varName() != null)
            defaultVar = GetEqName(context.varName().eqName());
        var defaultExpr = Visit(context.exprSingle());

        return new TypeswitchExpression
        {
            Operand = operand,
            Cases = cases,
            Default = new TypeswitchDefault { Variable = defaultVar, Result = defaultExpr },
            Location = GetLocation(context)
        };
    }

    private TypeswitchCase BuildTypeswitchCase(XQueryParserType.TypeswitchCaseClauseContext ctx)
    {
        QName? variable = null;
        if (ctx.varName() != null)
            variable = GetEqName(ctx.varName().eqName());

        var types = ctx.sequenceTypeUnion().sequenceType()
            .Select(BuildSequenceType).ToList();

        return new TypeswitchCase
        {
            Variable = variable,
            Types = types,
            Result = Visit(ctx.exprSingle())
        };
    }

    public override XQueryExpression VisitTryCatchExpr(XQueryParserType.TryCatchExprContext context)
    {
        var tryExpr = Visit(context.enclosedExpr().expr()!);
        var catches = context.catchClause().Select(BuildCatchClause).ToList();

        return new TryCatchExpression
        {
            TryExpression = tryExpr,
            CatchClauses = catches,
            Location = GetLocation(context)
        };
    }

    private CatchClause BuildCatchClause(XQueryParserType.CatchClauseContext ctx)
    {
        var errorCodes = ctx.catchErrorList().nameTest()
            .Select(nt =>
            {
                if (nt.eqName() != null)
                {
                    var name = GetEqName(nt.eqName());
                    return new NameTest { LocalName = name.LocalName, Prefix = name.Prefix };
                }
                // Wildcard
                return new NameTest { LocalName = "*" };
            }).ToList();

        return new CatchClause
        {
            ErrorCodes = errorCodes,
            Expression = Visit(ctx.enclosedExpr().expr()!)
        };
    }

    // ==================== Constructors ====================

    public override XQueryExpression VisitMapConstructor(XQueryParserType.MapConstructorContext context)
    {
        var entries = context.mapConstructorEntry().Select(e =>
        {
            var exprs = e.exprSingle();
            return new MapEntry
            {
                Key = Visit(exprs[0]),
                Value = Visit(exprs[1])
            };
        }).ToList();

        return new MapConstructor
        {
            Entries = entries,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitSquareArrayConstructor(XQueryParserType.SquareArrayConstructorContext context)
    {
        var members = context.exprSingle().Select(Visit).ToList();
        return new ArrayConstructor
        {
            Kind = ArrayConstructorKind.Square,
            Members = members,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitCurlyArrayConstructor(XQueryParserType.CurlyArrayConstructorContext context)
    {
        var enclosed = context.enclosedExpr().expr();
        var members = enclosed != null ? new List<XQueryExpression> { Visit(enclosed) } : [];
        return new ArrayConstructor
        {
            Kind = ArrayConstructorKind.Curly,
            Members = members,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitDirElemConstructor(XQueryParserType.DirElemConstructorContext context)
    {
        return BuildDirectElement(context.startTagBody(), context.dirElemContent(), GetLocation(context));
    }

    public override XQueryExpression VisitDirElemContent(XQueryParserType.DirElemContentContext context)
    {
        // Nested element with ELEM_CONTENT_OPEN_TAG (not top-level dirElemConstructor)
        if (context.startTagBody() != null)
            return BuildDirectElement(context.startTagBody(), context.dirElemContent(), GetLocation(context));

        if (context.dirElemConstructor() != null)
            return Visit(context.dirElemConstructor());

        if (context.dirEnclosedExpr() != null)
        {
            var expr = context.dirEnclosedExpr().expr();
            return expr != null ? Visit(expr) : new SequenceExpression { Items = [], Location = GetLocation(context) };
        }

        if (context.ElementContentChar() != null)
            return new StringLiteral { Value = context.ElementContentChar().GetText(), Location = GetLocation(context) };

        if (context.ELEM_CONTENT_ESCAPE_LBRACE() != null)
            return new StringLiteral { Value = "{", Location = GetLocation(context) };

        if (context.ELEM_CONTENT_ESCAPE_RBRACE() != null)
            return new StringLiteral { Value = "}", Location = GetLocation(context) };

        throw new InvalidOperationException("Unknown dirElemContent alternative");
    }

    private ElementConstructor BuildDirectElement(
        XQueryParserType.StartTagBodyContext startTag,
        XQueryParserType.DirElemContentContext[] contentItems,
        SourceLocation location)
    {
        var name = ParseQNameFromToken(startTag.START_TAG_QNAME().GetText());

        var attrs = startTag.dirAttribute()
            .Select(a =>
            {
                var attrName = ParseQNameFromToken(a.START_TAG_QNAME().GetText());
                var attrVal = new StringLiteral { Value = UnquoteString(a.START_TAG_STRING().GetText()) };
                return (XQueryExpression)new AttributeConstructor
                {
                    Name = attrName,
                    Value = attrVal,
                    Location = GetLocation(a)
                };
            }).ToList();

        var content = contentItems
            .Select(c => Visit(c))
            .ToList();

        return new ElementConstructor
        {
            Name = name,
            Attributes = attrs,
            Content = content,
            Location = location
        };
    }

    /// <summary>
    /// Parses a QName from a raw lexer token text like "prefix:local" or "local".
    /// </summary>
    private static QName ParseQNameFromToken(string text)
    {
        var colonIndex = text.IndexOf(':');
        if (colonIndex >= 0)
        {
            var prefix = text[..colonIndex];
            var local = text[(colonIndex + 1)..];
            return new QName(NamespaceId.None, local, prefix);
        }
        return new QName(NamespaceId.None, text, "");
    }

    public override XQueryExpression VisitCompDocConstructor(XQueryParserType.CompDocConstructorContext context)
    {
        return new DocumentConstructor
        {
            Content = Visit(context.enclosedExpr().expr()!),
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitCompElemConstructor(XQueryParserType.CompElemConstructorContext context)
    {
        var nameExpr = context.eqName() != null
            ? (XQueryExpression)new StringLiteral { Value = GetEqName(context.eqName()).LocalName }
            : Visit(context.enclosedExpr()[0].expr()!);

        // Content enclosed expression index depends on whether name is a literal or computed
        var contentEnclosedIndex = context.eqName() != null ? 0 : 1;
        var contentEnclosed = context.enclosedExpr()[contentEnclosedIndex];
        var contentExpr = contentEnclosed.expr() != null
            ? Visit(contentEnclosed.expr())
            : new SequenceExpression { Items = [] };

        return new ComputedElementConstructor
        {
            NameExpression = nameExpr,
            ContentExpression = contentExpr,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitCompAttrConstructor(XQueryParserType.CompAttrConstructorContext context)
    {
        var nameExpr = context.eqName() != null
            ? (XQueryExpression)new StringLiteral { Value = GetEqName(context.eqName()).LocalName }
            : Visit(context.enclosedExpr()[0].expr()!);

        var valueExpr = context.eqName() != null
            ? Visit(context.enclosedExpr()[0].expr()!)
            : Visit(context.enclosedExpr()[1].expr()!);

        return new ComputedAttributeConstructor
        {
            NameExpression = nameExpr,
            ValueExpression = valueExpr,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitCompTextConstructor(XQueryParserType.CompTextConstructorContext context)
    {
        return new TextConstructor
        {
            Value = Visit(context.enclosedExpr().expr()!),
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitCompCommentConstructor(XQueryParserType.CompCommentConstructorContext context)
    {
        return new CommentConstructor
        {
            Value = Visit(context.enclosedExpr().expr()!),
            IsDirect = false,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitCompPIConstructor(XQueryParserType.CompPIConstructorContext context)
    {
        string? directTarget = null;
        XQueryExpression? targetExpr = null;
        if (context.ncName() != null)
            directTarget = GetNcNameText(context.ncName());
        else
            targetExpr = Visit(context.enclosedExpr()[0].expr()!);

        var valueExpr = directTarget != null
            ? Visit(context.enclosedExpr()[0].expr()!)
            : Visit(context.enclosedExpr()[1].expr()!);

        return new PIConstructor
        {
            DirectTarget = directTarget,
            TargetExpression = targetExpr,
            Value = valueExpr,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitCompNamespaceConstructor(XQueryParserType.CompNamespaceConstructorContext context)
    {
        string? directPrefix = null;
        XQueryExpression? prefixExpr = null;
        if (context.ncName() != null)
            directPrefix = GetNcNameText(context.ncName());
        else
            prefixExpr = Visit(context.enclosedExpr()[0].expr()!);

        var uriExpr = directPrefix != null
            ? Visit(context.enclosedExpr()[0].expr()!)
            : Visit(context.enclosedExpr()[1].expr()!);

        return new NamespaceConstructor
        {
            DirectPrefix = directPrefix,
            PrefixExpression = prefixExpr,
            UriExpression = uriExpr,
            Location = GetLocation(context)
        };
    }

    public override XQueryExpression VisitOrderedExpr(XQueryParserType.OrderedExprContext context)
    {
        return Visit(context.enclosedExpr().expr()!);
    }

    public override XQueryExpression VisitUnorderedExpr(XQueryParserType.UnorderedExprContext context)
    {
        return Visit(context.enclosedExpr().expr()!);
    }

    // ==================== XPath 4.0: Record Constructor ====================

    public override XQueryExpression VisitRecordConstructor(XQueryParserType.RecordConstructorContext context)
    {
        var fields = new List<(string Name, XQueryExpression Value)>();
        foreach (var entry in context.recordConstructorEntry())
        {
            var name = GetNcNameText(entry.ncName());
            var value = Visit(entry.exprSingle());
            fields.Add((name, value));
        }
        return new RecordConstructorExpression { Fields = fields, Location = GetLocation(context) };
    }

    // ==================== XQuery 3.1/4.0: String Constructor ====================

    public override XQueryExpression VisitStringConstructor(XQueryParserType.StringConstructorContext context)
    {
        var parts = new List<StringConstructorPart>();
        foreach (var content in context.stringConstructorContent())
        {
            if (content.STRING_CONSTRUCTOR_CONTENT() != null)
            {
                parts.Add(new StringConstructorLiteralPart { Value = content.STRING_CONSTRUCTOR_CONTENT().GetText() });
            }
            else if (content.STRING_CONSTRUCTOR_INTERPOLATION_OPEN() != null)
            {
                var expr = Visit(content.expr());
                parts.Add(new StringConstructorInterpolationPart { Expression = expr });
            }
        }
        return new StringConstructorExpression { Parts = parts, Location = GetLocation(context) };
    }

    // ==================== XQuery Full-Text ====================

    public override XQueryExpression VisitFtContainsExpr(XQueryParserType.FtContainsExprContext context)
    {
        var baseExpr = Visit(context.otherwiseExpr());

        // If no "contains text" clause, just return the base expression
        if (context.KW_CONTAINS() == null)
            return baseExpr;

        var selection = BuildFtSelection(context.ftSelection());
        var matchOptions = context.ftMatchOptions() != null
            ? BuildFtMatchOptions(context.ftMatchOptions())
            : null;

        return new FtContainsExpression
        {
            Source = baseExpr,
            Selection = selection,
            MatchOptions = matchOptions,
            Location = GetLocation(context)
        };
    }

    private FtSelectionNode BuildFtSelection(XQueryParserType.FtSelectionContext ctx)
    {
        var or = BuildFtOr(ctx.ftOr());
        var posFilters = ctx.ftPosFilter().Select(BuildFtPosFilter).ToList();

        if (posFilters.Count > 0)
            return new FtSelectionWithFilters { Selection = or, PositionFilters = posFilters };
        return or;
    }

    private FtSelectionNode BuildFtOr(XQueryParserType.FtOrContext ctx)
    {
        var ands = ctx.ftAnd().Select(BuildFtAnd).ToList();
        if (ands.Count == 1) return ands[0];
        return new FtOrNode { Operands = ands };
    }

    private FtSelectionNode BuildFtAnd(XQueryParserType.FtAndContext ctx)
    {
        var nots = ctx.ftMildNot().Select(BuildFtMildNot).ToList();
        if (nots.Count == 1) return nots[0];
        return new FtAndNode { Operands = nots };
    }

    private FtSelectionNode BuildFtMildNot(XQueryParserType.FtMildNotContext ctx)
    {
        var primaries = ctx.ftUnaryNot().Select(BuildFtUnaryNot).ToList();
        if (primaries.Count == 1) return primaries[0];
        // "not in" is mild not — exclude terms that appear in the second operand
        return new FtMildNotNode { Include = primaries[0], Exclude = primaries[1] };
    }

    private FtSelectionNode BuildFtUnaryNot(XQueryParserType.FtUnaryNotContext ctx)
    {
        var primary = BuildFtPrimary(ctx.ftPrimary());
        if (ctx.KW_FTNOT() != null)
            return new FtNotNode { Operand = primary };
        return primary;
    }

    private FtSelectionNode BuildFtPrimary(XQueryParserType.FtPrimaryContext ctx)
    {
        if (ctx.ftWords() != null)
        {
            var wordsCtx = ctx.ftWords().ftWordsValue();
            string? searchText = null;
            XQueryExpression? searchExpr = null;

            if (wordsCtx.StringLiteral() != null)
                searchText = wordsCtx.StringLiteral().GetText().Trim('\'', '"');
            else if (wordsCtx.expr() != null)
                searchExpr = Visit(wordsCtx.expr());

            var anyAll = FtAnyAllOption.Any; // default
            if (ctx.ftAnyAllOption() != null)
            {
                var opt = ctx.ftAnyAllOption();
                if (opt.KW_ALL() != null) anyAll = opt.KW_WORDS() != null ? FtAnyAllOption.AllWords : FtAnyAllOption.All;
                else if (opt.KW_ANY() != null) anyAll = opt.KW_WORD() != null ? FtAnyAllOption.AnyWord : FtAnyAllOption.Any;
                else if (opt.KW_PHRASE() != null) anyAll = FtAnyAllOption.Phrase;
            }

            return new FtWordsNode { Text = searchText, Expression = searchExpr, Mode = anyAll };
        }

        // Parenthesized selection
        if (ctx.ftSelection() != null)
            return BuildFtSelection(ctx.ftSelection());

        return new FtWordsNode { Text = "" };
    }

    private FtPositionFilter BuildFtPosFilter(XQueryParserType.FtPosFilterContext ctx)
    {
        if (ctx.KW_ORDERED() != null)
            return new FtPositionFilter { Type = FtPositionFilterType.Ordered };
        if (ctx.KW_WINDOW() != null)
            return new FtPositionFilter { Type = FtPositionFilterType.Window, Value = int.Parse(ctx.IntegerLiteral().GetText()) };
        if (ctx.KW_DISTANCE() != null)
            return new FtPositionFilter { Type = FtPositionFilterType.Distance, Value = int.Parse(ctx.IntegerLiteral().GetText()) };
        if (ctx.KW_SAME() != null)
            return new FtPositionFilter { Type = ctx.KW_SENTENCE() != null ? FtPositionFilterType.SameSentence : FtPositionFilterType.SameParagraph };
        if (ctx.KW_ENTIRE() != null)
            return new FtPositionFilter { Type = FtPositionFilterType.EntireContent };
        if (ctx.KW_AT() != null)
            return new FtPositionFilter { Type = ctx.KW_START() != null ? FtPositionFilterType.AtStart : FtPositionFilterType.AtEnd };
        return new FtPositionFilter { Type = FtPositionFilterType.Ordered };
    }

    private FtMatchOptions BuildFtMatchOptions(XQueryParserType.FtMatchOptionsContext ctx)
    {
        var options = new FtMatchOptions();
        foreach (var opt in ctx.ftMatchOption())
        {
            if (opt.KW_STEMMING() != null)
                options.Stemming = opt.KW_NO() == null;
            else if (opt.KW_LANGUAGE() != null && opt.StringLiteral().Length > 0)
                options.Language = opt.StringLiteral()[0].GetText().Trim('\'', '"');
            else if (opt.KW_WILDCARDS() != null)
                options.Wildcards = opt.KW_NO() == null;
            else if (opt.KW_CASE() != null)
                options.CaseSensitive = opt.KW_SENSITIVE() != null;
            else if (opt.KW_DIACRITICS() != null)
                options.DiacriticsSensitive = opt.KW_SENSITIVE() != null;
            else if (opt.KW_STOP() != null && opt.KW_NO() != null)
                options.NoStopWords = true;
            else if (opt.KW_STOP() != null)
                options.StopWords = opt.StringLiteral().Select(s => s.GetText().Trim('\'', '"')).ToList();
            else if (opt.KW_THESAURUS() != null && opt.StringLiteral().Length > 0)
                options.Thesaurus = opt.StringLiteral()[0].GetText().Trim('\'', '"');
        }
        return options;
    }

    // ==================== XQuery Update Facility ====================

    public override XQueryExpression VisitInsertInto(XQueryParserType.InsertIntoContext ctx)
        => new InsertExpression { Source = Visit(ctx.exprSingle(0)), Target = Visit(ctx.exprSingle(1)), Position = InsertPosition.Into, Location = GetLocation(ctx) };
    public override XQueryExpression VisitInsertAsFirst(XQueryParserType.InsertAsFirstContext ctx)
        => new InsertExpression { Source = Visit(ctx.exprSingle(0)), Target = Visit(ctx.exprSingle(1)), Position = InsertPosition.AsFirstInto, Location = GetLocation(ctx) };
    public override XQueryExpression VisitInsertAsLast(XQueryParserType.InsertAsLastContext ctx)
        => new InsertExpression { Source = Visit(ctx.exprSingle(0)), Target = Visit(ctx.exprSingle(1)), Position = InsertPosition.AsLastInto, Location = GetLocation(ctx) };
    public override XQueryExpression VisitInsertBefore(XQueryParserType.InsertBeforeContext ctx)
        => new InsertExpression { Source = Visit(ctx.exprSingle(0)), Target = Visit(ctx.exprSingle(1)), Position = InsertPosition.Before, Location = GetLocation(ctx) };
    public override XQueryExpression VisitInsertAfter(XQueryParserType.InsertAfterContext ctx)
        => new InsertExpression { Source = Visit(ctx.exprSingle(0)), Target = Visit(ctx.exprSingle(1)), Position = InsertPosition.After, Location = GetLocation(ctx) };
    public override XQueryExpression VisitInsertNodesInto(XQueryParserType.InsertNodesIntoContext ctx)
        => new InsertExpression { Source = Visit(ctx.exprSingle(0)), Target = Visit(ctx.exprSingle(1)), Position = InsertPosition.Into, Location = GetLocation(ctx) };
    public override XQueryExpression VisitInsertNodesAsFirst(XQueryParserType.InsertNodesAsFirstContext ctx)
        => new InsertExpression { Source = Visit(ctx.exprSingle(0)), Target = Visit(ctx.exprSingle(1)), Position = InsertPosition.AsFirstInto, Location = GetLocation(ctx) };
    public override XQueryExpression VisitInsertNodesAsLast(XQueryParserType.InsertNodesAsLastContext ctx)
        => new InsertExpression { Source = Visit(ctx.exprSingle(0)), Target = Visit(ctx.exprSingle(1)), Position = InsertPosition.AsLastInto, Location = GetLocation(ctx) };
    public override XQueryExpression VisitInsertNodesBefore(XQueryParserType.InsertNodesBeforeContext ctx)
        => new InsertExpression { Source = Visit(ctx.exprSingle(0)), Target = Visit(ctx.exprSingle(1)), Position = InsertPosition.Before, Location = GetLocation(ctx) };
    public override XQueryExpression VisitInsertNodesAfter(XQueryParserType.InsertNodesAfterContext ctx)
        => new InsertExpression { Source = Visit(ctx.exprSingle(0)), Target = Visit(ctx.exprSingle(1)), Position = InsertPosition.After, Location = GetLocation(ctx) };

    public override XQueryExpression VisitDeleteNode(XQueryParserType.DeleteNodeContext ctx)
        => new DeleteExpression { Target = Visit(ctx.exprSingle()), Location = GetLocation(ctx) };
    public override XQueryExpression VisitDeleteNodes(XQueryParserType.DeleteNodesContext ctx)
        => new DeleteExpression { Target = Visit(ctx.exprSingle()), Location = GetLocation(ctx) };

    public override XQueryExpression VisitReplaceNode(XQueryParserType.ReplaceNodeContext ctx)
        => new ReplaceNodeExpression { Target = Visit(ctx.exprSingle(0)), Replacement = Visit(ctx.exprSingle(1)), Location = GetLocation(ctx) };
    public override XQueryExpression VisitReplaceValue(XQueryParserType.ReplaceValueContext ctx)
        => new ReplaceValueExpression { Target = Visit(ctx.exprSingle(0)), Value = Visit(ctx.exprSingle(1)), Location = GetLocation(ctx) };

    public override XQueryExpression VisitRenameExpr(XQueryParserType.RenameExprContext ctx)
        => new RenameExpression { Target = Visit(ctx.exprSingle(0)), NewName = Visit(ctx.exprSingle(1)), Location = GetLocation(ctx) };

    public override XQueryExpression VisitTransformExpr(XQueryParserType.TransformExprContext ctx)
    {
        var varNames = ctx.varName();
        var exprs = ctx.exprSingle();
        var bindings = new List<TransformCopyBinding>();
        // First N-2 exprSingle are copy bindings (paired with varNames)
        // Second-to-last is modify, last is return
        for (var i = 0; i < varNames.Length; i++)
            bindings.Add(new TransformCopyBinding { Variable = GetEqName(varNames[i].eqName()), Expression = Visit(exprs[i]) });

        return new TransformExpression
        {
            CopyBindings = bindings,
            ModifyExpr = Visit(exprs[varNames.Length]),
            ReturnExpr = Visit(exprs[varNames.Length + 1]),
            Location = GetLocation(ctx)
        };
    }

    // ==================== Type Helpers ====================

    private XdmSequenceType BuildSequenceType(XQueryParserType.SequenceTypeContext ctx)
    {
        if (ctx is XQueryParserType.EmptySequenceTypeContext)
            return XdmSequenceType.Empty;

        var itemSeqCtx = (XQueryParserType.ItemSequenceTypeContext)ctx;
        var (itemType, unprefixedTypeName) = BuildItemTypeWithInfo(itemSeqCtx.itemType());
        var occurrence = Occurrence.ExactlyOne;

        if (itemSeqCtx.occurrenceIndicator() != null)
        {
            var oi = itemSeqCtx.occurrenceIndicator();
            if (oi.QUESTION() != null) occurrence = Occurrence.ZeroOrOne;
            else if (oi.STAR() != null) occurrence = Occurrence.ZeroOrMore;
            else if (oi.PLUS() != null) occurrence = Occurrence.OneOrMore;
        }

        // Extract type annotation and names from element/attribute/document-node tests
        PhoenixmlDb.Xdm.XdmTypeName? typeAnnotation = null;
        string? elementName = null;
        string? attributeName = null;
        string? documentElementName = null;
        var kindTestCtx = itemSeqCtx.itemType().kindTest();
        if (kindTestCtx?.elementTest() is { } elemTest)
        {
            // Extract element name (first eqName when no STAR)
            if (elemTest.STAR() == null && elemTest.eqName().Length > 0)
                elementName = GetEqName(elemTest.eqName(0)).LocalName;
            // Extract type annotation (second eqName, or first when STAR)
            if (elemTest.COMMA() != null)
            {
                var typeNameIdx = elemTest.STAR() != null ? 0 : 1;
                if (elemTest.eqName().Length > typeNameIdx)
                    typeAnnotation = BuildTypeName(elemTest.eqName(typeNameIdx));
            }
        }
        else if (kindTestCtx?.attributeTest() is { } attrTest)
        {
            // Extract attribute name (first eqName when no STAR)
            if (attrTest.STAR() == null && attrTest.eqName().Length > 0)
                attributeName = GetEqName(attrTest.eqName(0)).LocalName;
            // Extract type annotation
            if (attrTest.COMMA() != null)
            {
                var typeNameIdx = attrTest.STAR() != null ? 0 : 1;
                if (attrTest.eqName().Length > typeNameIdx)
                    typeAnnotation = BuildTypeName(attrTest.eqName(typeNameIdx));
            }
        }
        else if (kindTestCtx?.documentTest() is { } docTest && docTest.elementTest() is { } docElemTest)
        {
            // document-node(element(name)) or document-node(element(name, type))
            if (docElemTest.STAR() == null && docElemTest.eqName().Length > 0)
                documentElementName = GetEqName(docElemTest.eqName(0)).LocalName;
        }

        // Extract typed function type info: function(T1, T2, ...) as ReturnType
        IReadOnlyList<XdmSequenceType>? functionParameterTypes = null;
        XdmSequenceType? functionReturnType = null;
        var itemTypeCtx = itemSeqCtx.itemType();
        if (itemType == ItemType.Function && itemTypeCtx.KW_FUNCTION() != null
            && itemTypeCtx.KW_AS() != null)
        {
            // Typed function test: function(T1, T2, ...) as ReturnType
            // The sequenceType children include parameter types + the return type (last one after KW_AS)
            var seqTypes = itemTypeCtx.sequenceType();
            if (seqTypes.Length > 0)
            {
                // Last sequenceType is the return type (after "as")
                functionReturnType = BuildSequenceType(seqTypes[^1]);
                // All but last are parameter types
                functionParameterTypes = seqTypes[..^1].Select(BuildSequenceType).ToArray();
            }
            else
            {
                // function() as ReturnType — zero parameters
                functionParameterTypes = Array.Empty<XdmSequenceType>();
            }
        }

        // Extract record field definitions (XPath 4.0)
        IReadOnlyDictionary<string, RecordFieldDef>? recordFields = null;
        var recordExtensible = false;
        if (itemType == ItemType.Record && itemTypeCtx.recordFieldDecl().Length > 0)
        {
            var fields = new Dictionary<string, RecordFieldDef>();
            foreach (var fieldCtx in itemTypeCtx.recordFieldDecl())
            {
                var fieldName = GetNcNameText(fieldCtx.ncName());
                var isOptional = fieldCtx.QUESTION() != null;
                XdmSequenceType? fieldType = null;
                if (fieldCtx.KW_AS() != null && fieldCtx.sequenceType() != null)
                    fieldType = BuildSequenceType(fieldCtx.sequenceType());
                fields[fieldName] = new RecordFieldDef { Name = fieldName, Type = fieldType, Optional = isOptional };
            }
            // Check for trailing * (extensible record)
            recordExtensible = itemTypeCtx.STAR() != null && itemTypeCtx.KW_RECORD() != null
                && itemTypeCtx.recordFieldDecl().Length > 0;
        }

        // Extract union member types (XPath 4.0)
        IReadOnlyList<XdmSequenceType>? unionTypes = null;
        if (itemType == ItemType.Union && itemTypeCtx.sequenceType().Length > 0)
        {
            unionTypes = itemTypeCtx.sequenceType().Select(BuildSequenceType).ToList();
        }

        // Extract enum values (XPath 4.0)
        IReadOnlyList<string>? enumValues = null;
        if (itemType == ItemType.Enum && itemTypeCtx.StringLiteral().Length > 0)
        {
            enumValues = itemTypeCtx.StringLiteral()
                .Select(s => s.GetText().Trim('\'', '"'))
                .ToList();
        }

        return new XdmSequenceType
        {
            ItemType = itemType, Occurrence = occurrence, TypeAnnotation = typeAnnotation,
            ElementName = elementName, AttributeName = attributeName, DocumentElementName = documentElementName,
            UnprefixedTypeName = unprefixedTypeName,
            FunctionParameterTypes = functionParameterTypes, FunctionReturnType = functionReturnType,
            RecordFields = recordFields, RecordExtensible = recordExtensible,
            EnumValues = enumValues, UnionTypes = unionTypes
        };
    }

    private static PhoenixmlDb.Xdm.XdmTypeName BuildTypeName(XQueryParserType.EqNameContext ctx)
    {
        var name = GetEqName(ctx);
        // Map common XSD type local names to NamespaceId.Xsd
        return new PhoenixmlDb.Xdm.XdmTypeName(PhoenixmlDb.Core.NamespaceId.Xsd, name.LocalName);
    }

    private (ItemType type, string? unprefixedTypeName) BuildItemTypeWithInfo(XQueryParserType.ItemTypeContext ctx)
    {
        if (ctx.KW_ITEM() != null) return (ItemType.Item, null);
        if (ctx.kindTest() != null) return (BuildKindTestItemType(ctx.kindTest()), null);
        // Check map/array/function BEFORE atomicOrUnionType because parameterized forms
        // like map(xs:string, xs:integer) have atomicOrUnionType as a child (the key type),
        // which would incorrectly match if checked first.
        if (ctx.KW_MAP() != null) return (ItemType.Map, null);
        if (ctx.KW_ARRAY() != null) return (ItemType.Array, null);
        if (ctx.KW_FUNCTION() != null) return (ItemType.Function, null);
        if (ctx.KW_RECORD() != null) return (ItemType.Record, null);
        if (ctx.KW_ENUM() != null) return (ItemType.Enum, null);
        if (ctx.KW_UNION() != null && ctx.sequenceType().Length > 0) return (ItemType.Union, null);
        if (ctx.atomicOrUnionType() != null) return BuildAtomicType(ctx.atomicOrUnionType());
        if (ctx.parenthesizedItemType() != null) return BuildItemTypeWithInfo(ctx.parenthesizedItemType().itemType());
        return (ItemType.Item, null);
    }

    private ItemType BuildItemType(XQueryParserType.ItemTypeContext ctx)
        => BuildItemTypeWithInfo(ctx).type;

    private static ItemType BuildKindTestItemType(XQueryParserType.KindTestContext ctx)
    {
        if (ctx.anyKindTest() != null) return ItemType.Node;
        if (ctx.elementTest() != null) return ItemType.Element;
        if (ctx.attributeTest() != null) return ItemType.Attribute;
        if (ctx.textTest() != null) return ItemType.Text;
        if (ctx.commentTest() != null) return ItemType.Comment;
        if (ctx.piTest() != null) return ItemType.ProcessingInstruction;
        if (ctx.documentTest() != null) return ItemType.Document;
        if (ctx.namespaceNodeTest() != null) return ItemType.Node;
        return ItemType.Node;
    }

    private static (ItemType type, string? unprefixedName) BuildAtomicType(XQueryParserType.AtomicOrUnionTypeContext ctx)
    {
        var name = GetEqName(ctx.eqName());
        var localName = name.LocalName;
        // Track whether the type name was unprefixed (no xs: prefix and no EQName {uri} syntax)
        var isUnprefixed = name.Prefix == null && name.ExpandedNamespace == null;

        var itemType = localName switch
        {
            "string" => ItemType.String,
            "boolean" => ItemType.Boolean,
            "integer" => ItemType.Integer,
            "decimal" => ItemType.Decimal,
            "double" => ItemType.Double,
            "float" => ItemType.Float,
            "date" => ItemType.Date,
            "dateTime" => ItemType.DateTime,
            "time" => ItemType.Time,
            "duration" => ItemType.Duration,
            "dayTimeDuration" => ItemType.DayTimeDuration,
            "yearMonthDuration" => ItemType.YearMonthDuration,
            "QName" => ItemType.QName,
            "anyURI" => ItemType.AnyUri,
            "anyAtomicType" => ItemType.AnyAtomicType,
            "gYearMonth" => ItemType.GYearMonth,
            "gYear" => ItemType.GYear,
            "gMonthDay" => ItemType.GMonthDay,
            "gDay" => ItemType.GDay,
            "gMonth" => ItemType.GMonth,
            "hexBinary" => ItemType.HexBinary,
            "base64Binary" => ItemType.Base64Binary,
            "untypedAtomic" => ItemType.UntypedAtomic,
            // Derived string types
            "normalizedString" or "token" or "language" or "NMTOKEN"
                or "Name" or "NCName" or "ID" or "IDREF" or "ENTITY" => ItemType.String,
            // Derived integer types
            "long" or "int" or "short" or "byte"
                or "nonNegativeInteger" or "positiveInteger"
                or "nonPositiveInteger" or "negativeInteger"
                or "unsignedLong" or "unsignedInt" or "unsignedShort" or "unsignedByte" => ItemType.Integer,
            "anyType" or "anySimpleType" or "untyped" or "NOTATION"
                => throw new XQueryParseException($"XPST0051: '{localName}' is not an atomic type and cannot be used as a cast/castable target"),
            _ => ItemType.AnyAtomicType
        };
        return (itemType, isUnprefixed ? localName : null);
    }

    private XdmSequenceType BuildSingleType(XQueryParserType.SingleTypeContext ctx)
    {
        var (itemType, unprefixedName) = BuildAtomicType(ctx.atomicOrUnionType());
        var occurrence = ctx.QUESTION() != null ? Occurrence.ZeroOrOne : Occurrence.ExactlyOne;
        return new XdmSequenceType { ItemType = itemType, Occurrence = occurrence, UnprefixedTypeName = unprefixedName };
    }

    // ==================== Name Helpers ====================

    private static QName GetEqName(XQueryParserType.EqNameContext ctx)
    {
        // Handle URIQualifiedName: Q{namespace-uri}local-name
        var uriQualified = ctx.URIQualifiedName();
        if (uriQualified != null)
        {
            var text = uriQualified.GetText(); // e.g. "Q{http://example.com}localname"
            var braceOpen = text.IndexOf('{');
            var braceClose = text.IndexOf('}');
            var namespaceUri = text[(braceOpen + 1)..braceClose];
            var localName = text[(braceClose + 1)..];
            return new QName(NamespaceId.None, localName, "") { ExpandedNamespace = namespaceUri };
        }

        var qName = ctx.qName();
        if (qName.prefixedName() != null)
        {
            var prefix = GetNcNameText(qName.prefixedName().ncName()[0]);
            var local = GetNcNameText(qName.prefixedName().ncName()[1]);
            return MakeQName(local, prefix);
        }
        var unprefixed = GetNcNameText(qName.unprefixedName().ncName());
        return MakeQName(unprefixed);
    }

    private static string GetNcNameText(XQueryParserType.NcNameContext ctx)
    {
        // NCName can be an actual NCName token or a keyword used as a name
        return ctx.GetText();
    }

    // ==================== Binary Expression Helpers ====================

    private XQueryExpression BuildLeftAssociativeBinary<T>(
        T[] operands,
        Func<int, BinaryOperator> opSelector,
        ParserRuleContext ctx) where T : ParserRuleContext
    {
        if (operands.Length == 1)
            return Visit(operands[0]);

        var result = Visit(operands[0]);
        for (int i = 1; i < operands.Length; i++)
        {
            result = new BinaryExpression
            {
                Left = result,
                Operator = opSelector(i - 1),
                Right = Visit(operands[i]),
                Location = GetLocation(ctx)
            };
        }
        return result;
    }

    private XQueryExpression BuildLeftAssociativeBinaryFromTokens<T>(
        T[] operands,
        IList<IParseTree> children,
        Func<ITerminalNode, BinaryOperator> tokenToOp,
        ParserRuleContext ctx) where T : ParserRuleContext
    {
        if (operands.Length == 1)
            return Visit(operands[0]);

        // Collect operator tokens between operands
        var ops = new List<BinaryOperator>();
        foreach (var child in children)
        {
            if (child is ITerminalNode tn && tn.Symbol.Type != XQueryLexer.LPAREN && tn.Symbol.Type != XQueryLexer.RPAREN)
            {
                try
                {
                    ops.Add(tokenToOp(tn));
                }
                catch (InvalidOperationException)
                {
                    // Not an operator token — skip
                }
            }
        }

        var result = Visit(operands[0]);
        for (int i = 1; i < operands.Length; i++)
        {
            var op = i - 1 < ops.Count ? ops[i - 1] : ops[0];
            result = new BinaryExpression
            {
                Left = result,
                Operator = op,
                Right = Visit(operands[i]),
                Location = GetLocation(ctx)
            };
        }
        return result;
    }
}
