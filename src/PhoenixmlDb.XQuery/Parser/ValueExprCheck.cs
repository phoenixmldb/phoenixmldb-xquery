using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using P = PhoenixmlDb.XQuery.Parser.Grammar.XQueryParser;

namespace PhoenixmlDb.XQuery.Parser;

/// <summary>
/// A validate expression and an extension expression are ValueExprs, not PrimaryExprs
/// (XQuery 3.1 §A.1: ValueExpr ::= ValidateExpr | ExtensionExpr | SimpleMapExpr): one may be
/// an operand of an operator, but not the start of a postfix, path or simple map expression,
/// which needs parentheses around it. The grammar reads them as primary expressions, so
/// validate { … }/* was accepted (QT3 qischema90007 expects XPST0003).
/// </summary>
internal static class ValueExprCheck
{
    public static void Check(IParseTree tree)
    {
        if (tree is not ParserRuleContext rule)
            return;
        if (rule is P.PrimaryExprContext primary
            && (primary.validateExpr() ?? (ParserRuleContext?)primary.extensionExpr()) is { } value
            && !StandsAlone(primary))
        {
            throw new XQueryParseException([new ParseError(
                $"XPST0003: A{(value is P.ValidateExprContext ? " validate" : "n extension")} expression must be parenthesized to be followed by a predicate, a path step, a lookup, '!' or an argument list",
                value.Start.Line, value.Start.Column)]);
        }
        for (var i = 0; i < rule.ChildCount; i++)
            Check(rule.GetChild(i));
    }

    /// <summary>True when the primary is the whole of the simple map expression it is in.</summary>
    private static bool StandsAlone(P.PrimaryExprContext primary)
        => primary.Parent is P.PostfixExprContext { ChildCount: 1 } postfix
           && postfix.Parent is P.StepExprContext step
           && step.Parent is P.RelativePathExprContext { ChildCount: 1 } relative
           && relative.Parent is P.RelativePathContext path
           && path.Parent is P.SimpleMapExprContext { ChildCount: 1 };
}
