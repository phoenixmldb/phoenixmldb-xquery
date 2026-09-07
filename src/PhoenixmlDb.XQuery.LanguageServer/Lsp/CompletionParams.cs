using System.Text.Json.Serialization;

namespace PhoenixmlDb.XQuery.LanguageServer.Lsp;

public sealed record CompletionParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")] Position Position);
