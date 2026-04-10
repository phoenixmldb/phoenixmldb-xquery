using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Binds variable references to their declarations.
/// </summary>
public sealed class VariableBinder : XQueryExpressionWalker
{
    private readonly StaticContext _context;
    private readonly Stack<Dictionary<string, VariableBinding>> _scopes = new();
    private readonly List<AnalysisError> _errors = [];

    public VariableBinder(StaticContext context)
    {
        _context = context;
        _scopes.Push(new Dictionary<string, VariableBinding>());
    }

    /// <summary>
    /// Binds all variable references in the expression.
    /// </summary>
    public XQueryExpression Bind(XQueryExpression expression, List<AnalysisError> errors)
    {
        _errors.Clear();
        Walk(expression);
        errors.AddRange(_errors);
        return expression;
    }

    public override object? VisitModuleExpression(ModuleExpression expr)
    {
        // Process declarations first (variable declarations bind their names)
        foreach (var decl in expr.Declarations)
        {
            if (decl is VariableDeclarationExpression varDecl)
            {
                // Walk the initializer expression (null for external variables with no default)
                if (!varDecl.IsExternal && varDecl.Value != null)
                    Walk(varDecl.Value);

                // Bind the variable in the current scope
                BindVariable(varDecl.Name, new VariableBinding
                {
                    Name = varDecl.Name,
                    Type = varDecl.TypeDeclaration ?? XdmSequenceType.ZeroOrMoreItems,
                    Scope = VariableScope.Global
                });
            }
            else if (decl is FunctionDeclarationExpression funcDecl)
            {
                // Walk function body with its parameters in scope
                PushScope();
                foreach (var param in funcDecl.Parameters)
                {
                    BindVariable(param.Name, new VariableBinding
                    {
                        Name = param.Name,
                        Type = param.Type ?? XdmSequenceType.ZeroOrMoreItems,
                        Scope = VariableScope.Parameter
                    });
                }
                Walk(funcDecl.Body);
                PopScope();
            }
            else
            {
                Walk(decl);
            }
        }

        // Then process the body
        Walk(expr.Body);
        return null;
    }

    public override object? VisitVariableReference(VariableReference expr)
    {
        var binding = LookupVariable(expr.Name);
        if (binding == null)
        {
            _errors.Add(new AnalysisError(
                XQueryErrorCodes.XPST0008,
                $"Variable ${expr.Name.LocalName} is not defined",
                expr.Location));
        }
        else
        {
            expr.ResolvedBinding = binding;
        }
        return null;
    }

    public override object? VisitFlworExpression(FlworExpression expr)
    {
        PushScope();

        foreach (var clause in expr.Clauses)
        {
            switch (clause)
            {
                case ForClause fc:
                    foreach (var binding in fc.Bindings)
                    {
                        // First walk the expression (in outer scope)
                        Walk(binding.Expression);

                        // Then bind the variable
                        BindVariable(binding.Variable, new VariableBinding
                        {
                            Name = binding.Variable,
                            Type = binding.TypeDeclaration ?? XdmSequenceType.Item,
                            Scope = VariableScope.For
                        });

                        // Bind positional variable if present
                        if (binding.PositionalVariable is { } positionalVar)
                        {
                            BindVariable(positionalVar, new VariableBinding
                            {
                                Name = positionalVar,
                                Type = XdmSequenceType.Integer,
                                Scope = VariableScope.Positional
                            });
                        }
                    }
                    break;

                case LetClause lc:
                    foreach (var binding in lc.Bindings)
                    {
                        // First walk the expression
                        Walk(binding.Expression);

                        // Then bind the variable
                        BindVariable(binding.Variable, new VariableBinding
                        {
                            Name = binding.Variable,
                            Type = binding.TypeDeclaration ?? XdmSequenceType.ZeroOrMoreItems,
                            Scope = VariableScope.Let
                        });
                    }
                    break;

                case WhereClause wc:
                    Walk(wc.Condition);
                    break;

                case OrderByClause obc:
                    foreach (var spec in obc.OrderSpecs)
                        Walk(spec.Expression);
                    break;

                case GroupByClause gbc:
                    foreach (var spec in gbc.GroupingSpecs)
                    {
                        if (spec.Expression != null)
                        {
                            // New-style: group by $v := expr — introduces a new variable in scope
                            // for subsequent clauses and the return expression.
                            Walk(spec.Expression);
                            BindVariable(spec.Variable, new VariableBinding
                            {
                                Name = spec.Variable,
                                Type = XdmSequenceType.ZeroOrMoreItems,
                                Scope = VariableScope.Let
                            });
                        }
                        // Plain `group by $v` rebinds an existing variable — leave as-is.
                    }
                    break;

                case CountClause cc:
                    BindVariable(cc.Variable, new VariableBinding
                    {
                        Name = cc.Variable,
                        Type = XdmSequenceType.Integer,
                        Scope = VariableScope.For
                    });
                    break;

                case WindowClause wc:
                    Walk(wc.Expression);
                    // Bind the main window variable ($w)
                    BindVariable(wc.Variable, new VariableBinding
                    {
                        Name = wc.Variable,
                        Type = wc.TypeDeclaration ?? XdmSequenceType.ZeroOrMoreItems,
                        Scope = VariableScope.For
                    });
                    // Bind window start/end condition variables ($s, $e, $spos, etc.)
                    // Per XQuery 3.1 §3.12.4, these are in scope for subsequent clauses and return
                    BindWindowConditionVariables(wc.Start);
                    Walk(wc.Start.When);
                    if (wc.End != null)
                    {
                        BindWindowConditionVariables(wc.End);
                        Walk(wc.End.When);
                    }
                    break;
            }
        }

        Walk(expr.ReturnExpression);

        PopScope();
        return null;
    }

    private void BindWindowConditionVariables(WindowCondition cond)
    {
        if (cond.CurrentItem is { } cur)
            BindVariable(cur, new VariableBinding { Name = cur, Type = XdmSequenceType.Item, Scope = VariableScope.For });
        if (cond.Position is { } pos)
            BindVariable(pos, new VariableBinding { Name = pos, Type = XdmSequenceType.Integer, Scope = VariableScope.For });
        if (cond.PreviousItem is { } prev)
            BindVariable(prev, new VariableBinding { Name = prev, Type = XdmSequenceType.OptionalItem, Scope = VariableScope.For });
        if (cond.NextItem is { } next)
            BindVariable(next, new VariableBinding { Name = next, Type = XdmSequenceType.OptionalItem, Scope = VariableScope.For });
    }

    public override object? VisitQuantifiedExpression(QuantifiedExpression expr)
    {
        PushScope();

        foreach (var binding in expr.Bindings)
        {
            Walk(binding.Expression);
            BindVariable(binding.Variable, new VariableBinding
            {
                Name = binding.Variable,
                Type = binding.TypeDeclaration ?? XdmSequenceType.Item,
                Scope = VariableScope.Quantified
            });
        }

        Walk(expr.Satisfies);

        PopScope();
        return null;
    }

    public override object? VisitInlineFunctionExpression(InlineFunctionExpression expr)
    {
        PushScope();

        foreach (var param in expr.Parameters)
        {
            BindVariable(param.Name, new VariableBinding
            {
                Name = param.Name,
                Type = param.Type ?? XdmSequenceType.ZeroOrMoreItems,
                Scope = VariableScope.Parameter
            });
        }

        Walk(expr.Body);

        PopScope();
        return null;
    }

    public override object? VisitTypeswitchExpression(TypeswitchExpression expr)
    {
        Walk(expr.Operand);

        foreach (var @case in expr.Cases)
        {
            if (@case.Variable is { } caseVar)
            {
                PushScope();
                BindVariable(caseVar, new VariableBinding
                {
                    Name = caseVar,
                    Type = @case.Types.FirstOrDefault() ?? XdmSequenceType.Item,
                    Scope = VariableScope.TypeswitchVariable
                });
                Walk(@case.Result);
                PopScope();
            }
            else
            {
                Walk(@case.Result);
            }
        }

        if (expr.Default.Variable is { } defaultVar)
        {
            PushScope();
            BindVariable(defaultVar, new VariableBinding
            {
                Name = defaultVar,
                Type = XdmSequenceType.Item,
                Scope = VariableScope.TypeswitchVariable
            });
            Walk(expr.Default.Result);
            PopScope();
        }
        else
        {
            Walk(expr.Default.Result);
        }

        return null;
    }

    public override object? VisitTryCatchExpression(TryCatchExpression expr)
    {
        Walk(expr.TryExpression);

        foreach (var @catch in expr.CatchClauses)
        {
            PushScope();
            // Bind implicit err:* variables per XQuery 3.1 §3.15.1
            // Use the actual err namespace ID so lookups match namespace-resolved references.
            var errNsId = _context.Namespaces.GetOrCreateId(WellKnownNamespaces.ErrUri);
            var errVarType = XdmSequenceType.ZeroOrMoreItems;
            foreach (var errVar in new[] { "code", "description", "value", "module", "line-number", "column-number", "additional" })
            {
                var qname = new QName(errNsId, errVar, "err") { ExpandedNamespace = WellKnownNamespaces.ErrUri };
                BindVariable(qname, new VariableBinding
                {
                    Name = qname,
                    Type = errVarType,
                    Scope = VariableScope.Let
                });
            }
            Walk(@catch.Expression);
            PopScope();
        }

        return null;
    }

    public override object? VisitTransformExpression(TransformExpression expr)
    {
        PushScope();

        foreach (var binding in expr.CopyBindings)
        {
            // Walk the source expression first
            Walk(binding.Expression);

            // Bind the copy variable
            BindVariable(binding.Variable, new VariableBinding
            {
                Name = binding.Variable,
                Type = XdmSequenceType.ZeroOrMoreItems,
                Scope = VariableScope.Let
            });
        }

        Walk(expr.ModifyExpr);
        Walk(expr.ReturnExpr);

        PopScope();
        return null;
    }

    private void PushScope()
    {
        _scopes.Push(new Dictionary<string, VariableBinding>());
    }

    private void PopScope()
    {
        _scopes.Pop();
    }

    private void BindVariable(QName name, VariableBinding binding)
    {
        var key = _context.MakeVariableKey(name);
        _scopes.Peek()[key] = binding;
    }

    private VariableBinding? LookupVariable(QName name)
    {
        var key = _context.MakeVariableKey(name);
        foreach (var scope in _scopes)
        {
            if (scope.TryGetValue(key, out var binding))
                return binding;
        }

        // Check global variables from prolog declarations
        if (_context.GlobalVariables.TryGetValue(key, out var globalBinding))
            return globalBinding;

        // Fallback: also try a local-name-only key so $p:v can find $v and vice-versa
        // only when both resolve to the empty/default namespace (handled by MakeVariableKey).
        return null;
    }
}
