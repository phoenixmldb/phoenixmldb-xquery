using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:empty($array as array(*)) as xs:boolean — XPath 4.0. Tests whether an array has no
/// members.
/// </summary>
/// <remarks>
/// Distinct from the zero-arity <see cref="ArrayEmptyFunction"/> below, which CONSTRUCTS an
/// empty array. Same name, different arity and meaning — the predicate was missing, so
/// <c>array:empty($a)</c> resolved to nothing and reported "Unknown function: empty#1" even
/// though a class called ArrayEmptyFunction existed.
///
/// Found by Dimitre Novatchev's Generators library, which calls the predicate three times.
/// </remarks>
public sealed class ArrayEmpty1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "empty");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?>;
        return ValueTask.FromResult<object?>((array?.Count ?? 0) == 0);
    }
}
