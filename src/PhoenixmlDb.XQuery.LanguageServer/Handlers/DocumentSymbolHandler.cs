using System;
using System.Collections.Generic;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.LanguageServer.Lsp;
using PhoenixmlDb.XQuery.Parser;
using Range = PhoenixmlDb.XQuery.LanguageServer.Lsp.Range;
using Position = PhoenixmlDb.XQuery.LanguageServer.Lsp.Position;

namespace PhoenixmlDb.XQuery.LanguageServer.Handlers;

/// <summary>
/// Walks the XQuery AST and emits one <see cref="DocumentSymbol"/> per top-level
/// declaration (function, variable, namespace, module import). MVP: prolog
/// declarations only — body expressions are not deep-walked. Parse failures
/// produce an empty list.
/// </summary>
public static class DocumentSymbolHandler
{
    /// <summary>Returns the document's top-level symbols, or empty when the buffer fails to parse.</summary>
    public static DocumentSymbol[] Handle(DocumentBuffer buf)
    {
        ArgumentNullException.ThrowIfNull(buf);

        XQueryExpression ast;
        try
        {
            ast = new XQueryParserFacade().Parse(buf.Text);
        }
        catch (XQueryParseException) { return Array.Empty<DocumentSymbol>(); }

        var symbols = new List<DocumentSymbol>();
        if (ast is ModuleExpression module)
        {
            foreach (var decl in module.Declarations)
                Collect(decl, symbols);
        }
        else
        {
            // Function-only or variable-only files may parse to a bare declaration.
            Collect(ast, symbols);
        }
        return symbols.ToArray();
    }

    private static void Collect(XQueryExpression decl, List<DocumentSymbol> sink)
    {
        switch (decl)
        {
            case FunctionDeclarationExpression fn:
                sink.Add(new DocumentSymbol(
                    Name: fn.Name.LocalName,
                    Kind: SymbolKind.Function,
                    Range: ToRange(fn.Location),
                    SelectionRange: ToRange(fn.Location)));
                break;
            case VariableDeclarationExpression vd:
                sink.Add(new DocumentSymbol(
                    Name: "$" + vd.Name.LocalName,
                    Kind: SymbolKind.Variable,
                    Range: ToRange(vd.Location),
                    SelectionRange: ToRange(vd.Location)));
                break;
            case NamespaceDeclarationExpression ns:
                sink.Add(new DocumentSymbol(
                    Name: ns.Prefix,
                    Kind: SymbolKind.Namespace,
                    Range: ToRange(ns.Location),
                    SelectionRange: ToRange(ns.Location)));
                break;
            case ModuleImportExpression mi:
                sink.Add(new DocumentSymbol(
                    Name: mi.Prefix ?? mi.NamespaceUri,
                    Kind: SymbolKind.Module,
                    Range: ToRange(mi.Location),
                    SelectionRange: ToRange(mi.Location)));
                break;
        }
    }

    private static Range ToRange(SourceLocation? loc)
    {
        if (loc is null) return new Range(new Position(0, 0), new Position(0, 1));
        var line = Math.Max(0, loc.Line - 1);
        var col = Math.Max(0, loc.Column);
        return new Range(new Position(line, col), new Position(line, col + Math.Max(1, loc.Length)));
    }
}
