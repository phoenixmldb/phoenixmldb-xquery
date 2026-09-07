using System.Text.Json.Serialization;

namespace PhoenixmlDb.XQuery.LanguageServer.Lsp;

/// <summary>LSP 3.17 SymbolKind enum values used by this MVP.</summary>
public static class SymbolKind
{
    public const int Function = 12;
    public const int Variable = 13;
    public const int Namespace = 3;
    public const int Module = 2;
}

// Plan 29 additions
