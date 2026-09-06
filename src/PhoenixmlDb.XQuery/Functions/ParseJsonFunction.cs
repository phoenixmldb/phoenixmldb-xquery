using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:parse-json($json-text as xs:string) as item()?
/// Parses a JSON string and returns the corresponding XDM value
/// (map for objects, array for arrays, atomic for primitives).
/// </summary>
public sealed class ParseJsonFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-json");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "json-text"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return ParseJsonCore(arguments[0]?.ToString(), null, context);
    }

    internal static ValueTask<object?> ParseJsonCore(string? jsonText, ParseJsonOptions? options,
        Ast.ExecutionContext? context)
    {
        if (jsonText == null)
            return ValueTask.FromResult<object?>(null);

        var opts = options ?? new ParseJsonOptions();

        try
        {
            // Pre-process to handle lone surrogates that System.Text.Json rejects
            var processed = JsonToXdmConverter.PreProcessSurrogates(jsonText);
            using var doc = JsonDocument.Parse(processed);
            // JsonDocument is disposable; we must materialize the result before disposing.
            var result = JsonToXdmConverter.Convert(doc.RootElement, opts, context);
            return ValueTask.FromResult(result);
        }
        catch (XQueryRuntimeException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new XQueryRuntimeException("FOJS0001",
                $"The string supplied to fn:parse-json() is not valid JSON: {ex.Message}");
        }
    }
}
