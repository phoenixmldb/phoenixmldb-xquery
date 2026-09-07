using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Helper for converting arguments to sequences. Equivalent to
/// <see cref="XdmShape.SequenceItems"/>, which documents the sequence-versus-array convention
/// and the five bugs that came from hand-rolling it; prefer that for new code.
/// </summary>
internal static class SequenceHelper
{
    public static List<object?> Flatten(object? arg)
    {
        // XDM arrays (List<object?>) are single items — do not flatten
        if (arg is List<object?>) return [arg];
        if (arg is IEnumerable<object?> seq)
            return seq.ToList();
        if (arg == null) return [];
        return [arg];
    }
}
