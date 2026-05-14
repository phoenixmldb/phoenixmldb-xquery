using System.Text.Json.Serialization;

namespace PhoenixmlDb.XQuery.LanguageServer.Lsp;

// Minimal subset of LSP 3.17 message types used by this MVP server.
// Property names use camelCase (matched via [JsonPropertyName]) because LSP is camelCase.

public sealed record Position(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character);

public sealed record Range(
    [property: JsonPropertyName("start")] Position Start,
    [property: JsonPropertyName("end")] Position End);

public sealed record Location(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("range")] Range Range);

public sealed record Diagnostic(
    [property: JsonPropertyName("range")] Range Range,
    [property: JsonPropertyName("severity")] int Severity,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("message")] string Message);

public sealed record TextDocumentIdentifier(
    [property: JsonPropertyName("uri")] string Uri);

public sealed record VersionedTextDocumentIdentifier(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("version")] int Version);

public sealed record TextDocumentItem(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("languageId")] string LanguageId,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("text")] string Text);

public sealed record TextDocumentContentChangeEvent(
    [property: JsonPropertyName("range")] Range? Range,
    [property: JsonPropertyName("text")] string Text);

public sealed record DidOpenTextDocumentParams(
    [property: JsonPropertyName("textDocument")] TextDocumentItem TextDocument);

public sealed record DidChangeTextDocumentParams(
    [property: JsonPropertyName("textDocument")] VersionedTextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("contentChanges")] TextDocumentContentChangeEvent[] ContentChanges);

public sealed record DidCloseTextDocumentParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument);

public sealed record PublishDiagnosticsParams(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("diagnostics")] Diagnostic[] Diagnostics);

public sealed record HoverParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")] Position Position);

public sealed record Hover(
    [property: JsonPropertyName("contents")] string Contents);

public sealed record CompletionParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")] Position Position);

public sealed record CompletionItem(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("kind")] int Kind);

public sealed record CompletionList(
    [property: JsonPropertyName("isIncomplete")] bool IsIncomplete,
    [property: JsonPropertyName("items")] CompletionItem[] Items);

public sealed record ServerCapabilities
{
    [JsonPropertyName("textDocumentSync")]
    public int TextDocumentSync { get; init; } = 1; // Full sync (MVP — simpler than incremental)

    [JsonPropertyName("hoverProvider")]
    public bool HoverProvider { get; init; }

    [JsonPropertyName("completionProvider")]
    public CompletionOptions? CompletionProvider { get; init; }
}

public sealed record CompletionOptions(
    [property: JsonPropertyName("triggerCharacters")] string[] TriggerCharacters);

public sealed record InitializeResult(
    [property: JsonPropertyName("capabilities")] ServerCapabilities Capabilities);
