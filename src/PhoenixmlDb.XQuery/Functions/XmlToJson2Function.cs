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
/// fn:xml-to-json($input, $options) as xs:string? — 2-arg version
/// </summary>
public sealed class XmlToJson2Function : XQueryFunction
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public override QName Name => new(FunctionNamespaces.Fn, "xml-to-json");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalNode },
        new() { Name = new QName(NamespaceId.None, "options"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Validate options map
        bool indent = false;
        var options = arguments[1];
        if (options is IDictionary<object, object?> map)
        {
            if (map.TryGetValue("indent", out var indentVal))
            {
                if (indentVal is bool ib)
                    indent = ib;
                else
                    throw context.Error("XPTY0004", "Option 'indent' must be a boolean value");
            }
            if (map.TryGetValue("validate", out var validateVal))
            {
                if (validateVal is not bool)
                    throw context.Error("XPTY0004", "Option 'validate' must be a boolean value");
                if (validateVal is true)
                    throw context.Error("FOJS0004", "Option 'validate' is true but the processor is not schema-aware");
            }
        }

        var input = arguments[0];
        if (input is null)
            return ValueTask.FromResult<object?>(null);

        var store = context.NodeStore;
        if (store is null)
            throw context.Error("FOJS0006", "xml-to-json requires a node store");

        var elem = XmlToJsonFunction.ResolveToElement(input, store);
        if (elem is null)
            return ValueTask.FromResult<object?>(null);

        var sb = new StringBuilder();
        XmlToJsonFunction.SerializeJsonElement(elem, store, sb);
        var result = sb.ToString();

        if (indent)
        {
            // Re-parse and pretty-print the JSON
            try
            {
                using var doc = JsonDocument.Parse(result);
                result = JsonSerializer.Serialize(doc.RootElement, IndentedJsonOptions);
            }
            catch { /* If re-parsing fails, return the unindented version */ }
        }

        return ValueTask.FromResult<object?>(result);
    }
}

// ─── fn:json-to-xml (1-arg) ────────────────────────────────────────────────
