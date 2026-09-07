using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:index-of($seq, $search) as xs:integer*
/// </summary>
public sealed class IndexOfFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "index-of");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "search"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var search = QueryExecutionContext.Atomize(arguments[1]);

        // Per spec: $search must be a single atomic value
        if (search == null || (search is object?[] searchArr && searchArr.Length == 0))
            throw context.Error("XPTY0004", "fn:index-of() search value must be a single atomic value, got empty sequence");

        if (seq == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        // fn:index-of's $input is xs:anyAtomicType* — arguments are atomized on call,
        // flattening XDM arrays into their atomic members (e.g.
        // fn:index-of([1,[5,6],[6,7]], 6) → (3, 4)).
        if (seq is List<object?> || (seq is IEnumerable<object?> probe && probe.Any(static x => x is List<object?>)))
            seq = DataFunction.Atomize(seq);

        var items = seq is IEnumerable<object?> s ? s : (IEnumerable<object?>)[seq];
        var result = new List<object>();
        var index = 0;

        foreach (var item in items)
        {
            index++;
            var atomized = QueryExecutionContext.Atomize(item);

            // NaN is never equal to anything (including NaN) per IEEE 754/XPath
            if (IsNaN(atomized) || IsNaN(search))
                continue;

            // fn:index-of compares each member against $search with the eq operator
            // (F&O §14.1.2). XQueryValueComparer implements those value-equality
            // semantics — numeric type promotion across xs:integer / xs:decimal /
            // xs:double (so 4 eq 04.0 is true), the string family (xs:string /
            // xs:untypedAtomic), and the date/time/duration families — matching
            // fn:distinct-values. Members whose type is not comparable with $search
            // simply do not match (no error, per the spec note). The default
            // (codepoint) collation is ordinal, which is what this comparer applies.
            //
            // One eq-vs-distinct-values divergence must be handled before delegating
            // to the comparer: under the eq operator an xs:untypedAtomic operand is
            // cast to the dynamic type of the *other* operand, so
            // xs:untypedAtomic("x") eq xs:anyURI("x") is true. The comparer follows
            // distinct-values semantics (xs:anyURI is its own distinct value, never
            // equal to a string), so it would reject that pair. When exactly one side
            // is untypedAtomic and the other is a string-family value (xs:string /
            // xs:anyURI / xs:token …), compare them as strings.
            if (StringFamilyEqualsWithUntyped(atomized, search))
                result.Add((long)index); // XPath uses 1-based indexing, xs:integer = long
            else if (XQueryValueComparer.Instance.Equals(atomized, search))
                result.Add((long)index);
        }

        return ValueTask.FromResult<object?>(result.ToArray());
    }

    /// <summary>
    /// True when exactly one operand is xs:untypedAtomic and the other is a
    /// string-family value, and their lexical values are codepoint-equal. Under the
    /// eq operator (which fn:index-of uses) an untypedAtomic operand is cast to the
    /// other operand's type, so it compares as a string against xs:string / xs:anyURI /
    /// xs:NMTOKEN / etc. Returns false when neither side is untypedAtomic so the
    /// general comparer (and its distinct-values anyURI rules) still governs.
    /// </summary>
    internal static bool StringFamilyEqualsWithUntyped(object? a, object? b)
    {
        bool aUntyped = a is Xdm.XsUntypedAtomic;
        bool bUntyped = b is Xdm.XsUntypedAtomic;
        if (aUntyped == bUntyped) return false; // both or neither untyped → not this case
        var sa = StringFamilyValue(a);
        var sb = StringFamilyValue(b);
        return sa != null && sb != null && string.Equals(sa, sb, StringComparison.Ordinal);
    }

    private static string? StringFamilyValue(object? v) => v switch
    {
        Xdm.XsUntypedAtomic ua => ua.Value,
        string s => s,
        Xdm.XsAnyUri uri => uri.Value,
        Xdm.XsTypedString ts => ts.Value,
        _ => null
    };

    private static bool IsNaN(object? value) =>
        (value is double d && double.IsNaN(d)) ||
        (value is float f && float.IsNaN(f));
}
