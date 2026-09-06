using System.Text.Json.Serialization;

namespace PhoenixmlDb.XQuery.LanguageServer.Lsp;

public sealed record TextDocumentIdentifier(
    [property: JsonPropertyName("uri")] string Uri);
