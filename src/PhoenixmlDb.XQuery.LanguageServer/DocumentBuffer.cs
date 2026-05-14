using System;

namespace PhoenixmlDb.XQuery.LanguageServer;

/// <summary>
/// Per-URI source-of-truth maintained by the LSP server: full text, version, and helpers
/// for converting between (line, character) positions and 0-based character offsets.
/// MVP uses full-text sync so every <c>didChange</c> replaces the entire buffer.
/// </summary>
public sealed class DocumentBuffer
{
    public DocumentBuffer(string uri, int version, string text)
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        Version = version;
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public string Uri { get; }
    public int Version { get; private set; }
    public string Text { get; private set; }

    /// <summary>Replaces the buffer text and updates the version.</summary>
    public void ReplaceAll(int version, string text)
    {
        Version = version;
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>
    /// Converts a 0-based character offset into an LSP <see cref="Lsp.Position"/>
    /// (0-based line, 0-based UTF-16 code unit within the line).
    /// </summary>
    public Lsp.Position OffsetToPosition(int offset)
    {
        if (offset < 0) offset = 0;
        if (offset > Text.Length) offset = Text.Length;
        int line = 0, col = 0;
        for (int i = 0; i < offset; i++)
        {
            if (Text[i] == '\n')
            {
                line++;
                col = 0;
            }
            else if (Text[i] == '\r')
            {
                // CRLF: count as one line break (handle the LF in the next iteration if present)
                if (i + 1 < Text.Length && Text[i + 1] == '\n')
                {
                    // do nothing; LF will increment
                }
                else
                {
                    line++;
                    col = 0;
                }
            }
            else
            {
                col++;
            }
        }
        return new Lsp.Position(line, col);
    }
}
