using System.Text.Json.Serialization;

namespace PhoenixmlDb.XQuery.LanguageServer.Lsp;

public sealed record CompletionList(
    [property: JsonPropertyName("isIncomplete")] bool IsIncomplete,
    [property: JsonPropertyName("items")] CompletionItem[] Items);
