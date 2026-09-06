using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:error($arg) — XSD 1.1 empty union type; cast always fails with FORG0001.</summary>
public sealed class ErrorConstructorFunction : TypeConstructorFunction
{
    public ErrorConstructorFunction() : base("error") { }

    // The xs:error constructor has signature function(xs:anyAtomicType?) as xs:error?.
    // Per XSD 1.1 / FO, xs:error? denotes the empty sequence, so the return type is
    // empty-sequence() (QT3 xs-error-006/007). The parameter is xs:anyAtomicType?.
    public override XdmSequenceType ReturnType => XdmSequenceType.Empty;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"),
                 Type = new XdmSequenceType { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrOne } }];

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        throw new Execution.XQueryRuntimeException("FORG0001",
            "Cannot cast to xs:error — xs:error has no values");
    }
}
