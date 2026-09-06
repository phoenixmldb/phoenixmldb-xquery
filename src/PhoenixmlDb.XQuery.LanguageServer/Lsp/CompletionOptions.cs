using System.Text.Json.Serialization;

namespace PhoenixmlDb.XQuery.LanguageServer.Lsp;

public sealed record CompletionOptions(
    [property: JsonPropertyName("triggerCharacters")] string[] TriggerCharacters);
