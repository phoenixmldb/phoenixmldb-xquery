using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.LanguageServer.Lsp;
using PhoenixmlDb.XQuery.Parser;
using StreamJsonRpc;
using Range = PhoenixmlDb.XQuery.LanguageServer.Lsp.Range;
using Position = PhoenixmlDb.XQuery.LanguageServer.Lsp.Position;

namespace PhoenixmlDb.XQuery.LanguageServer;

/// <summary>
/// JSON-RPC target implementing a minimal LSP server for XQuery. Wraps
/// <see cref="QueryEngine"/> compile + analysis to surface diagnostics on every
/// <c>textDocument/didChange</c>. Hover and completion return curated info from
/// the existing function library.
/// </summary>
/// <remarks>
/// MVP scope: full-text sync, no incremental edits. No semantic tokens, rename,
/// definition, references, formatting, code actions, signature help, or inlay hints.
/// </remarks>
public sealed class XQueryLanguageServer
{
    private readonly ConcurrentDictionary<string, DocumentBuffer> _buffers = new(StringComparer.Ordinal);

    /// <summary>Bound by <see cref="Program"/> to the RPC channel for publishing back to the client.</summary>
    public JsonRpc? Rpc { get; set; }

    [JsonRpcMethod("initialize")]
    public InitializeResult Initialize(object? _)
    {
        return new InitializeResult(new ServerCapabilities
        {
            TextDocumentSync = 1, // Full sync — simpler than incremental for MVP
            HoverProvider = true,
            CompletionProvider = new CompletionOptions(TriggerCharacters: [":", "$", "/"]),
        });
    }

    [JsonRpcMethod("initialized")]
    public void Initialized(object? _)
    {
        // No-op. Client tells us it's ready for notifications.
    }

    [JsonRpcMethod("shutdown")]
    public object? Shutdown(object? _) => null;

    [JsonRpcMethod("exit")]
    public void Exit(object? _) => Environment.Exit(0);

    [JsonRpcMethod("textDocument/didOpen")]
    public Task DidOpen(DidOpenTextDocumentParams p)
    {
        var buf = new DocumentBuffer(p.TextDocument.Uri, p.TextDocument.Version, p.TextDocument.Text);
        _buffers[p.TextDocument.Uri] = buf;
        return PublishDiagnosticsAsync(buf);
    }

    [JsonRpcMethod("textDocument/didChange")]
    public Task DidChange(DidChangeTextDocumentParams p)
    {
        if (!_buffers.TryGetValue(p.TextDocument.Uri, out var buf)) return Task.CompletedTask;
        if (p.ContentChanges.Length == 0) return Task.CompletedTask;
        // Full-sync: the last change is the entire new text.
        var last = p.ContentChanges[^1];
        buf.ReplaceAll(p.TextDocument.Version, last.Text);
        return PublishDiagnosticsAsync(buf);
    }

    [JsonRpcMethod("textDocument/didClose")]
    public void DidClose(DidCloseTextDocumentParams p)
    {
        _buffers.TryRemove(p.TextDocument.Uri, out _);
    }

    [JsonRpcMethod("textDocument/hover")]
    public Hover? Hover(HoverParams p)
    {
        // MVP: return null. A full implementation would identify the token under the
        // cursor and look it up in the function library / static context.
        return null;
    }

    [JsonRpcMethod("textDocument/completion")]
    public CompletionList Completion(CompletionParams p)
    {
        // MVP: return a fixed list of common fn:* names. The editor's own context
        // provider handles richer filtering; the server's job is to make external
        // editors (VS Code, etc.) usable.
        var names = new[]
        {
            "count", "string", "string-length", "concat", "contains", "starts-with",
            "ends-with", "normalize-space", "matches", "replace", "tokenize",
            "data", "distinct-values", "empty", "exists", "not", "true", "false",
            "sum", "min", "max", "abs", "floor", "ceiling", "round",
            "name", "local-name", "namespace-uri", "position", "last",
        };
        var items = new CompletionItem[names.Length];
        for (int i = 0; i < names.Length; i++)
            items[i] = new CompletionItem(names[i], Kind: 3); // 3 = Function
        return new CompletionList(IsIncomplete: false, Items: items);
    }

    private async Task PublishDiagnosticsAsync(DocumentBuffer buf)
    {
        var diags = ComputeDiagnostics(buf);
        if (Rpc is null) return;
        await Rpc.NotifyAsync("textDocument/publishDiagnostics",
            new PublishDiagnosticsParams(buf.Uri, diags)).ConfigureAwait(false);
    }

    /// <summary>
    /// Compiles <paramref name="buf"/> against a fresh <see cref="QueryEngine"/> and
    /// converts any <see cref="XQueryParseException"/> + analysis errors to LSP diagnostics.
    /// </summary>
    internal Diagnostic[] ComputeDiagnostics(DocumentBuffer buf)
    {
        var diags = new List<Diagnostic>();
        var engine = new QueryEngine();

        QueryCompilationResult result;
        try
        {
            result = engine.Compile(buf.Text);
        }
        catch (XQueryParseException ex)
        {
            // Parser bails out — surface as a single diagnostic. Best-effort range:
            // try to use the first ParseError, fall back to (0,0)-(0,1).
            var range = ex.Errors.Count > 0
                ? new Range(
                    new Position(Math.Max(0, ex.Errors[0].Line - 1), Math.Max(0, ex.Errors[0].Column)),
                    new Position(Math.Max(0, ex.Errors[0].Line - 1), Math.Max(0, ex.Errors[0].Column) + 1))
                : new Range(new Position(0, 0), new Position(0, 1));
            diags.Add(new Diagnostic(range, Severity: 1, Code: "XQST0003", Source: "xquery", Message: ex.Message));
            return diags.ToArray();
        }
        catch (XQueryException ex)
        {
            diags.Add(new Diagnostic(
                new Range(new Position(0, 0), new Position(0, 1)),
                Severity: 1, Code: ex.ErrorCode, Source: "xquery", Message: ex.Message));
            return diags.ToArray();
        }

        foreach (var err in result.Errors)
        {
            var range = err.Location is { } loc
                ? new Range(
                    new Position(Math.Max(0, loc.Line - 1), Math.Max(0, loc.Column)),
                    new Position(Math.Max(0, loc.Line - 1), Math.Max(0, loc.Column) + Math.Max(1, loc.Length)))
                : new Range(new Position(0, 0), new Position(0, 1));
            var severity = err.Severity == Analysis.AnalysisErrorSeverity.Error ? 1 : 2;
            diags.Add(new Diagnostic(range, severity, Code: err.Code, Source: "xquery", Message: err.Message));
        }

        return diags.ToArray();
    }
}
