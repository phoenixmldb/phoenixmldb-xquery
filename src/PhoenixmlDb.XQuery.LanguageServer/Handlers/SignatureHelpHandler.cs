using System;
using System.Collections.Generic;
using PhoenixmlDb.XQuery.LanguageServer.Lsp;

namespace PhoenixmlDb.XQuery.LanguageServer.Handlers;

/// <summary>
/// Server-side <c>textDocument/signatureHelp</c> handler. Walks back from the caret
/// to find the nearest unmatched <c>(</c>; reads the function name immediately before
/// it; looks up the signature in a curated table; counts commas at depth 0 between
/// <c>(</c> and the caret for the active-parameter index.
/// </summary>
public static class SignatureHelpHandler
{
    private static readonly Dictionary<string, string> Signatures = new(StringComparer.Ordinal)
    {
        ["abs"] = "abs($arg as xs:numeric?) as xs:numeric?",
        ["boolean"] = "boolean($arg as item()*) as xs:boolean",
        ["ceiling"] = "ceiling($arg as xs:numeric?) as xs:numeric?",
        ["concat"] = "concat($arg1, $arg2, …) as xs:string",
        ["contains"] = "contains($arg1 as xs:string?, $arg2 as xs:string?) as xs:boolean",
        ["count"] = "count($arg as item()*) as xs:integer",
        ["data"] = "data($arg as item()*) as xs:anyAtomicType*",
        ["distinct-values"] = "distinct-values($arg as xs:anyAtomicType*) as xs:anyAtomicType*",
        ["doc"] = "doc($uri as xs:string?) as document-node()?",
        ["empty"] = "empty($arg as item()*) as xs:boolean",
        ["ends-with"] = "ends-with($arg1 as xs:string?, $arg2 as xs:string?) as xs:boolean",
        ["exists"] = "exists($arg as item()*) as xs:boolean",
        ["floor"] = "floor($arg as xs:numeric?) as xs:numeric?",
        ["head"] = "head($arg as item()*) as item()?",
        ["last"] = "last() as xs:integer",
        ["local-name"] = "local-name($arg as node()?) as xs:string",
        ["lower-case"] = "lower-case($arg as xs:string?) as xs:string",
        ["matches"] = "matches($input as xs:string?, $pattern as xs:string) as xs:boolean",
        ["max"] = "max($arg as xs:anyAtomicType*) as xs:anyAtomicType?",
        ["min"] = "min($arg as xs:anyAtomicType*) as xs:anyAtomicType?",
        ["name"] = "name($arg as node()?) as xs:string",
        ["namespace-uri"] = "namespace-uri($arg as node()?) as xs:anyURI",
        ["normalize-space"] = "normalize-space($arg as xs:string?) as xs:string",
        ["not"] = "not($arg as item()*) as xs:boolean",
        ["number"] = "number($arg as xs:anyAtomicType?) as xs:double",
        ["position"] = "position() as xs:integer",
        ["replace"] = "replace($input as xs:string?, $pattern as xs:string, $replacement as xs:string) as xs:string",
        ["round"] = "round($arg as xs:numeric?) as xs:numeric?",
        ["starts-with"] = "starts-with($arg1 as xs:string?, $arg2 as xs:string?) as xs:boolean",
        ["string"] = "string($arg as item()?) as xs:string",
        ["string-join"] = "string-join($arg as xs:anyAtomicType*, $sep as xs:string?) as xs:string",
        ["string-length"] = "string-length($arg as xs:string?) as xs:integer",
        ["substring"] = "substring($source as xs:string?, $start as xs:double, $length as xs:double?) as xs:string",
        ["sum"] = "sum($arg as xs:anyAtomicType*, $zero as xs:anyAtomicType?) as xs:anyAtomicType?",
        ["tokenize"] = "tokenize($input as xs:string?, $pattern as xs:string) as xs:string*",
        ["upper-case"] = "upper-case($arg as xs:string?) as xs:string",
    };

    /// <summary>Returns signature info for the function call surrounding <paramref name="pos"/>, or null.</summary>
    public static SignatureHelp? Handle(DocumentBuffer buf, Position pos)
    {
        ArgumentNullException.ThrowIfNull(buf);
        ArgumentNullException.ThrowIfNull(pos);

        var offset = PositionToOffset(buf.Text, pos);
        if (offset <= 0) return null;

        int depth = 0;
        int activeParam = 0;
        int parenIndex = -1;
        for (int i = offset - 1; i >= 0; i--)
        {
            var c = buf.Text[i];
            if (c == ')') depth++;
            else if (c == ',' && depth == 0) activeParam++;
            else if (c == '(')
            {
                if (depth == 0) { parenIndex = i; break; }
                depth--;
            }
        }
        if (parenIndex <= 0) return null;

        int start = parenIndex;
        while (start > 0 && IsWordChar(buf.Text[start - 1])) start--;
        if (start == parenIndex) return null;
        var fnName = buf.Text.Substring(start, parenIndex - start);

        // Strip a leading "fn:" or "local:" prefix when looking up; we key by local name.
        var lookupName = StripPrefix(fnName);
        if (!Signatures.TryGetValue(lookupName, out var sig)) return null;

        return new SignatureHelp(
            Signatures: [new SignatureInformation(sig, Documentation: null)],
            ActiveSignature: 0,
            ActiveParameter: activeParam);
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':';

    private static string StripPrefix(string name)
    {
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        return colon >= 0 ? name[(colon + 1)..] : name;
    }

    private static int PositionToOffset(string text, Position pos)
    {
        int line = 0, col = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (line == pos.Line && col == pos.Character) return i;
            if (text[i] == '\n') { line++; col = 0; }
            else col++;
        }
        return text.Length;
    }
}
