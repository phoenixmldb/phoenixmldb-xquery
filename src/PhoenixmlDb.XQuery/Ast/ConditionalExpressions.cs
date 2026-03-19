using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// If-then-else expression.
/// </summary>
public sealed class IfExpression : XQueryExpression
{
    public required XQueryExpression Condition { get; init; }
    public required XQueryExpression Then { get; init; }
    /// <summary>
    /// Else branch. Null for XQuery 4.0 braced-if (defaults to empty sequence).
    /// </summary>
    public XQueryExpression? Else { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitIfExpression(this);

    public override string ToString()
        => Else != null
            ? $"if ({Condition}) then {Then} else {Else}"
            : $"if ({Condition}) {{ {Then} }}";
}

/// <summary>
/// Quantified expression (some/every $x in ... satisfies ...).
/// </summary>
public sealed class QuantifiedExpression : XQueryExpression
{
    public required Quantifier Quantifier { get; init; }
    public required IReadOnlyList<QuantifiedBinding> Bindings { get; init; }
    public required XQueryExpression Satisfies { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitQuantifiedExpression(this);

    public override string ToString()
    {
        var q = Quantifier == Quantifier.Some ? "some" : "every";
        var bindings = string.Join(", ", Bindings);
        return $"{q} {bindings} satisfies {Satisfies}";
    }
}

/// <summary>
/// Quantifier type.
/// </summary>
public enum Quantifier
{
    Some,
    Every
}

/// <summary>
/// Binding in quantified expression.
/// </summary>
public sealed class QuantifiedBinding
{
    public required QName Variable { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }
    public required XQueryExpression Expression { get; init; }

    public override string ToString()
    {
        var type = TypeDeclaration != null ? $" as {TypeDeclaration}" : "";
        return $"${Variable.LocalName}{type} in {Expression}";
    }
}

/// <summary>
/// Switch expression (XQuery 3.0+).
/// </summary>
public sealed class SwitchExpression : XQueryExpression
{
    public required XQueryExpression Operand { get; init; }
    public required IReadOnlyList<SwitchCase> Cases { get; init; }
    public required XQueryExpression Default { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitSwitchExpression(this);

    public override string ToString()
    {
        var cases = string.Join(" ", Cases);
        return $"switch ({Operand}) {cases} default return {Default}";
    }
}

/// <summary>
/// A case in a switch expression.
/// </summary>
public sealed class SwitchCase
{
    public required IReadOnlyList<XQueryExpression> Values { get; init; }
    public required XQueryExpression Result { get; init; }

    public override string ToString()
    {
        var values = string.Join(" ", Values.Select(v => $"case {v}"));
        return $"{values} return {Result}";
    }
}

/// <summary>
/// Typeswitch expression.
/// </summary>
public sealed class TypeswitchExpression : XQueryExpression
{
    public required XQueryExpression Operand { get; init; }
    public required IReadOnlyList<TypeswitchCase> Cases { get; init; }
    public required TypeswitchDefault Default { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitTypeswitchExpression(this);

    public override string ToString()
    {
        var cases = string.Join(" ", Cases);
        return $"typeswitch ({Operand}) {cases} default {Default}";
    }
}

/// <summary>
/// A case in a typeswitch expression.
/// </summary>
public sealed class TypeswitchCase
{
    public QName? Variable { get; init; }
    public required IReadOnlyList<XdmSequenceType> Types { get; init; }
    public required XQueryExpression Result { get; init; }

    public override string ToString()
    {
        var types = string.Join(" | ", Types);
        var variable = Variable != null ? $" ${Variable.Value.LocalName}" : "";
        return $"case{variable} {types} return {Result}";
    }
}

/// <summary>
/// Default case in a typeswitch expression.
/// </summary>
public sealed class TypeswitchDefault
{
    public QName? Variable { get; init; }
    public required XQueryExpression Result { get; init; }

    public override string ToString()
    {
        var variable = Variable != null ? $" ${Variable.Value.LocalName}" : "";
        return $"{variable} return {Result}";
    }
}

/// <summary>
/// Try-catch expression (XQuery 3.0+).
/// </summary>
public sealed class TryCatchExpression : XQueryExpression
{
    public required XQueryExpression TryExpression { get; init; }
    public required IReadOnlyList<CatchClause> CatchClauses { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitTryCatchExpression(this);

    public override string ToString()
    {
        var catches = string.Join(" ", CatchClauses);
        return $"try {{ {TryExpression} }} {catches}";
    }
}

/// <summary>
/// A catch clause in a try-catch expression.
/// </summary>
public sealed class CatchClause
{
    public required IReadOnlyList<NameTest> ErrorCodes { get; init; }
    public required XQueryExpression Expression { get; init; }

    public override string ToString()
    {
        var codes = ErrorCodes.Count > 0
            ? string.Join(" | ", ErrorCodes)
            : "*";
        return $"catch {codes} {{ {Expression} }}";
    }
}
