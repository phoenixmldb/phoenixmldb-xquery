using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:merge($maps as map(*)*, $options as map(*)) as map(*)
/// </summary>
public sealed class MapMerge2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "merge");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "maps"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ZeroOrMore } },
         new() { Name = new QName(NamespaceId.None, "options"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var duplicatesPolicy = "use-first";
        if (arguments.Count > 1)
        {
            if (arguments[1] is null)
                throw new XQueryRuntimeException("XPTY0004",
                    "Second argument to map:merge must be a map, got empty sequence");
            if (arguments[1] is IDictionary<object, object?> opts
                && opts.TryGetValue("duplicates", out var dup) && dup != null)
                duplicatesPolicy = dup.ToString()!;
        }

        var result = MapHelper.NewMap();

        void MergeMaps(IDictionary<object, object?> dict)
        {
            foreach (var (key, value) in dict)
            {
                if (result.ContainsKey(key))
                {
                    switch (duplicatesPolicy)
                    {
                        case "use-first": break;
                        case "use-last":
                        case "use-any":
                            MapKeyHelper.RemoveByKey(result, key);
                            result[key] = value;
                            break;
                        case "combine":
                            if (MapKeyHelper.TryGetValue(result, key, out var existing))
                            {
                                MapKeyHelper.RemoveByKey(result, key);
                                // Combine into a sequence (object?[]), NOT a List<object?> (which is XDM array)
                                if (existing is object?[] existingSeq)
                                {
                                    if (value is object?[] valSeq)
                                    {
                                        var combined = new object?[existingSeq.Length + valSeq.Length];
                                        existingSeq.CopyTo(combined, 0);
                                        valSeq.CopyTo(combined, existingSeq.Length);
                                        result[key] = combined;
                                    }
                                    else
                                    {
                                        var combined = new object?[existingSeq.Length + 1];
                                        existingSeq.CopyTo(combined, 0);
                                        combined[existingSeq.Length] = value;
                                        result[key] = combined;
                                    }
                                }
                                else
                                {
                                    if (value is object?[] valSeq2)
                                    {
                                        var combined = new object?[1 + valSeq2.Length];
                                        combined[0] = existing;
                                        valSeq2.CopyTo(combined, 1);
                                        result[key] = combined;
                                    }
                                    else
                                        result[key] = new object?[] { existing, value };
                                }
                            }
                            break;
                        case "reject":
                            throw new XQueryRuntimeException("FOJS0003",
                                $"Duplicate key in map:merge with duplicates='reject'");
                    }
                }
                else
                    result[key] = value;
            }
        }

        if (arguments[0] is IDictionary<object, object?> singleMap)
            MergeMaps(singleMap);
        else if (arguments[0] is IEnumerable<object?> maps)
            foreach (var map in maps)
                if (map is IDictionary<object, object?> dict)
                    MergeMaps(dict);

        return ValueTask.FromResult<object?>(result);
    }
}
