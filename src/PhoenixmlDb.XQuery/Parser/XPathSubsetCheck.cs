using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using P = PhoenixmlDb.XQuery.Parser.Grammar.XQueryParser;

namespace PhoenixmlDb.XQuery.Parser;

/// <summary>
/// Rejects, with XPST0003, the XQuery 3.1 syntax that XPath 3.1 does not have. The parser reads
/// one grammar for both languages, so an XPath expression parses wherever XQuery would; this pass
/// over the parse tree gives the XPath answer.
/// </summary>
/// <remarks>
/// Only the difference between the two 3.1 languages is checked. Syntax that 4.0 adds to both
/// (braced <c>if</c>, <c>-&gt;</c> functions, <c>for member</c>, records) is left to the 4.0 rules, as
/// in XQuery mode. Direct element constructors never reach here: in XPath mode the lexer reads
/// every <c>&lt;</c> as an operator.
/// </remarks>
internal static class XPathSubsetCheck
{
    public static void Check(P.ModuleContext module)
    {
        if (module.versionDecl() is { } version)
            Fail(version, "a version declaration");
        if (module.libraryModule() is { } library)
            Fail(library, "a library module");
        var prolog = module.mainModule()?.prolog();
        if (prolog is { ChildCount: > 0 })
            Fail(prolog, "a prolog");
        Walk(module);
    }

    private static void Walk(IParseTree tree)
    {
        if (tree is not ParserRuleContext rule)
            return;
        if (Construct(rule) is { } construct)
            Fail(rule, construct);
        for (var i = 0; i < rule.ChildCount; i++)
            Walk(rule.GetChild(i));
    }

    /// <summary>The XQuery-only construct <paramref name="rule"/> is, or null for XPath syntax.</summary>
    private static string? Construct(ParserRuleContext rule) => rule switch
    {
        // XPath's for and let expressions are a single clause and a return.
        P.FlworExprContext f when f.intermediateClause().Length > 0 => "a FLWOR clause after the first",
        P.FlworExprContext f when f.initialClause().windowClause() != null => "a window clause",
        P.ForBindingContext b when b.typeDeclaration() != null => "a type declaration on a for variable",
        P.ForBindingContext b when b.allowingEmpty() != null => "allowing empty",
        P.ForBindingContext b when b.positionalVar() != null => "a positional variable",
        P.LetBindingContext b when b.typeDeclaration() != null => "a type declaration on a let variable",
        P.QuantifiedBindingContext b when b.typeDeclaration() != null => "a type declaration on a quantified variable",
        P.SwitchExprContext => "a switch expression",
        P.TypeswitchExprContext => "a typeswitch expression",
        P.TryCatchExprContext => "a try/catch expression",
        P.InsertExprContext or P.DeleteExprContext or P.ReplaceExprContext
            or P.RenameExprContext or P.TransformExprContext => "an update expression",
        P.ValidateExprContext => "a validate expression",
        P.ExtensionExprContext => "an extension expression",
        P.OrderedExprContext or P.UnorderedExprContext => "an ordered/unordered expression",
        P.DirElemConstructorContext or P.DirPIConstructorContext
            or P.DirCommentConstructorContext => "a direct constructor",
        P.CompConstructorContext => "a computed constructor",
        P.StringConstructorContext => "a string constructor",
        P.AnnotationContext => "an annotation",
        _ => null
    };

    private static void Fail(ParserRuleContext at, string construct) =>
        throw new XQueryParseException([new ParseError(
            $"XPST0003: XPath does not have {construct}; it is XQuery syntax",
            at.Start.Line, at.Start.Column)]);
}
