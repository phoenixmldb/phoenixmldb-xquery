using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// xs:ENTITIES($arg) — XSD list type constructor.
/// Splits whitespace-separated string into a sequence of xs:ENTITY values.
/// </summary>
public sealed class EntitiesConstructorFunction : TypeConstructorFunction
{
    public EntitiesConstructorFunction() : base("ENTITIES") { }

    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var s = arg.ToString() ?? "";
        var tokens = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return ValueTask.FromResult<object?>(tokens.Cast<object?>().ToArray());
    }
}

// ──────────────────────────────────────────────
// Date/Time type constructors
// ──────────────────────────────────────────────
