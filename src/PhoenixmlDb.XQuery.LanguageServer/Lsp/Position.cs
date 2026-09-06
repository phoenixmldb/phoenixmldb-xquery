using System.Text.Json.Serialization;

namespace PhoenixmlDb.XQuery.LanguageServer.Lsp;


// Minimal subset of LSP 3.17 message types used by this MVP server.
// Property names use camelCase (matched via [JsonPropertyName]) because LSP is camelCase.

public sealed record Position(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character);
