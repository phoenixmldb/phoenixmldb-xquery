using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:normalizedString($arg)</summary>
public sealed class NormalizedStringConstructorFunction : TypeConstructorFunction
{
    public NormalizedStringConstructorFunction() : base("normalizedString") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Replace tabs, newlines, carriage returns with spaces
        var s = (arg.ToString() ?? "")
            .Replace('\t', ' ')
            .Replace('\n', ' ')
            .Replace('\r', ' ');
        return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsTypedString(s, "normalizedString"));
    }
}
