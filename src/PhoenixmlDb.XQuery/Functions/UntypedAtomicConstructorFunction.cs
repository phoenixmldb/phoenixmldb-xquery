using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:untypedAtomic($arg)</summary>
public sealed class UntypedAtomicConstructorFunction : TypeConstructorFunction
{
    public UntypedAtomicConstructorFunction() : base("untypedAtomic") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(new Xdm.XsUntypedAtomic(arg.ToString() ?? ""));
    }
}
