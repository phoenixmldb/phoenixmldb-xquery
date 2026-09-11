using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:json-to-xml($json-text as xs:string) as document-node()?
/// Converts a JSON string to the XML representation using the XPath functions namespace.
/// </summary>
public sealed class JsonToXmlFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "json-to-xml");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Node, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "json-text"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var jsonText = arguments[0]?.ToString();
        if (jsonText is null)
            return ValueTask.FromResult<object?>(null);

        var builder = context.NodeStore as INodeBuilder;
        if (builder is null)
            throw context.Error("FOJS0001", "json-to-xml requires a node builder");

        try
        {
            var doc = JsonToXmlConverter.Convert(jsonText, builder, liberal: false, duplicates: "retain",
                escape: false, fallback: null, baseUri: context.StaticBaseUri);
            return ValueTask.FromResult<object?>(doc);
        }
        catch (JsonException ex)
        {
            throw context.Error("FOJS0001", $"Invalid JSON: {ex.Message}");
        }
    }
}

// ─── fn:json-to-xml (2-arg) ────────────────────────────────────────────────
