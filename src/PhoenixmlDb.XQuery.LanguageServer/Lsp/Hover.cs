using System.Text.Json.Serialization;

namespace PhoenixmlDb.XQuery.LanguageServer.Lsp;

public sealed record Hover(
    [property: JsonPropertyName("contents")] string Contents);
