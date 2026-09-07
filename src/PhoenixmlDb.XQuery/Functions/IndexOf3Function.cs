using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:index-of($seq, $search, $collation) as xs:integer*
/// </summary>
public sealed class IndexOf3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "index-of");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "search"), Type = XdmSequenceType.Item },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var search = QueryExecutionContext.Atomize(arguments[1]);
        var collationArg = arguments[2];

        // Per spec: $collation is xs:string (exactly one)
        if (collationArg == null || (collationArg is object?[] ca && ca.Length == 0))
            throw context.Error("XPTY0004", "fn:index-of() collation argument must be a string, got empty sequence");

        var comparison = CollationHelper.GetStringComparison(collationArg.ToString());

        // Per spec: $search must be a single atomic value
        if (search == null || (search is object?[] searchArr && searchArr.Length == 0))
            throw context.Error("XPTY0004", "fn:index-of() search value must be a single atomic value, got empty sequence");

        if (seq == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        // fn:index-of's $input is xs:anyAtomicType* — arguments are atomized on call,
        // flattening XDM arrays into their atomic members.
        if (seq is List<object?> || (seq is IEnumerable<object?> probe && probe.Any(static x => x is List<object?>)))
            seq = DataFunction.Atomize(seq);

        var items = seq is IEnumerable<object?> s ? s : (IEnumerable<object?>)[seq];
        var result = new List<object>();
        var index = 0;

        foreach (var item in items)
        {
            index++;
            var atomized = QueryExecutionContext.Atomize(item);

            // String-like comparison: xs:string, xs:untypedAtomic, xs:anyURI all compare as strings
            var itemStr = atomized is string si ? si : atomized is Xdm.XsUntypedAtomic ua ? ua.Value : atomized is Xdm.XsAnyUri aau ? aau.Value : atomized is Xdm.XsTypedString tsi ? tsi.Value : null;
            var searchStr = search is string ss ? ss : search is Xdm.XsUntypedAtomic sua ? sua.Value : search is Xdm.XsAnyUri sau ? sau.Value : search is Xdm.XsTypedString tss ? tss.Value : null;

            if (itemStr != null && searchStr != null)
            {
                // String family honours the requested collation.
                if (string.Equals(itemStr, searchStr, comparison))
                    result.Add((long)index);
            }
            // Non-string members compare with the eq operator's value-equality
            // semantics (numeric type promotion, date/time/duration families) — the
            // collation is irrelevant for these. See IndexOfFunction for details.
            else if (XQueryValueComparer.Instance.Equals(atomized, search))
                result.Add((long)index);
        }

        return ValueTask.FromResult<object?>(result.ToArray());
    }
}
