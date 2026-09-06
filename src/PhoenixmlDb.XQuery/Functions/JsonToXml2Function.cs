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
/// fn:json-to-xml($json-text, $options) — 2-arg version
/// </summary>
public sealed class JsonToXml2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "json-to-xml");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Node, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "json-text"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "options"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne } }
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

        // Parse options map
        var liberal = false;
        var escape = false;
        var duplicates = "retain";
        Func<string, Task<string>>? fallbackFn = null;
        var options = arguments[1];
        if (options is IDictionary<object, object?> map)
        {
            // liberal option: must be xs:boolean
            if (map.TryGetValue("liberal", out var liberalVal))
            {
                if (liberalVal is bool lb)
                    liberal = lb;
                else if (liberalVal is null || liberalVal is object?[] arr && arr.Length == 0)
                    throw context.Error("FOJS0001", "Option 'liberal' must be a boolean value, got empty sequence");
                else
                    throw context.Error("FOJS0001", "Option 'liberal' must be a boolean value");
            }

            // validate option: must be xs:boolean
            if (map.TryGetValue("validate", out var validateVal))
            {
                if (validateVal is true)
                    throw context.Error("FOJS0004", "Option 'validate' is true but the processor is not schema-aware");
                if (validateVal is bool)
                { /* false — no action needed */ }
                else if (validateVal is null || validateVal is object?[] va && va.Length == 0)
                    throw context.Error("XPTY0004", "Option 'validate' must be a boolean value, got empty sequence");
                else if (validateVal is string)
                    throw context.Error("XPTY0004", "Option 'validate' must be a boolean value");
                else if (validateVal is object?[] va2 && va2.Length > 1)
                    throw context.Error("XPTY0004", "Option 'validate' must be a single boolean value");
                else
                    throw context.Error("XPTY0004", "Option 'validate' must be a boolean value");
            }

            // escape option: must be xs:boolean
            if (map.TryGetValue("escape", out var escapeVal))
            {
                if (escapeVal is bool eb)
                    escape = eb;
                else if (escapeVal is null || escapeVal is object?[] ea && ea.Length == 0)
                    throw context.Error("XPTY0004", "Option 'escape' must be a boolean value, got empty sequence");
                else if (escapeVal is string)
                    throw context.Error("XPTY0004", "Option 'escape' must be a boolean value");
                else if (escapeVal is object?[] ea2 && ea2.Length > 1)
                    throw context.Error("XPTY0004", "Option 'escape' must be a single boolean value");
                else
                    throw context.Error("XPTY0004", "Option 'escape' must be a boolean value");
            }

            // fallback option: must be a function with arity 1
            if (map.TryGetValue("fallback", out var fallbackVal))
            {
                if (fallbackVal is not XQueryFunction fallbackFunc)
                    throw context.Error("XPTY0004", "Option 'fallback' must be a function");
                // Validate arity: must accept exactly 1 argument
                if (fallbackFunc.Parameters.Count != 1)
                    throw context.Error("XPTY0004", "Option 'fallback' must be a function with arity 1");
                // escape=true + fallback is an error per spec (FOJS0005)
                if (escape)
                    throw context.Error("FOJS0005", "The 'fallback' option cannot be used when 'escape' is true");
                var capturedFn = fallbackFunc;
                var capturedCtx = context;
#pragma warning disable CA2008 // Task.ContinueWith without TaskScheduler — result used synchronously by caller
                fallbackFn = s => capturedFn.InvokeAsync(new object?[] { s }, capturedCtx)
                    .AsTask()
                    .ContinueWith(t => t.Result?.ToString() ?? "", TaskScheduler.Default);
#pragma warning restore CA2008
            }

            // duplicates option: must be xs:string with value use-first, retain, or reject
            if (map.TryGetValue("duplicates", out var dupVal))
            {
                if (dupVal is string ds)
                {
                    duplicates = ds switch
                    {
                        "use-first" or "retain" or "reject" => ds,
                        _ => throw context.Error("FOJS0005", $"Invalid value '{ds}' for option 'duplicates'; must be use-first, retain, or reject")
                    };
                }
                else
                    throw context.Error("XPTY0004", "Option 'duplicates' must be a string value");
            }
        }

        try
        {
            var doc = JsonToXmlConverter.Convert(jsonText, builder, liberal, duplicates, escape,
                fallback: fallbackFn, baseUri: context.StaticBaseUri);
            return ValueTask.FromResult<object?>(doc);
        }
        catch (JsonException ex)
        {
            throw context.Error("FOJS0001", $"Invalid JSON: {ex.Message}");
        }
    }
}

// ─── JsonToXmlConverter ────────────────────────────────────────────────────
