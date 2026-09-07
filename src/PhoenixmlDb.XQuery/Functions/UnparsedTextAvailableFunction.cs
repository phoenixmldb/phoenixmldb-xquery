using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:unparsed-text-available($href as xs:string?) as xs:boolean</summary>
public sealed class UnparsedTextAvailableFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "unparsed-text-available");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString }];

    public override async ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        RequireStringArgument(arguments[0], "href");
        var href = arguments[0]?.ToString();
        if (href is null) return false;
        try
        {
            // Per spec: returns true iff a call to unparsed-text with the same args would succeed
            await UnparsedTextFunction.ReadUnparsedText(href, null, context).ConfigureAwait(false);
            return true;
        }
        catch (XQueryRuntimeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates that a function argument is string-compatible (xs:string, xs:anyURI,
    /// xs:untypedAtomic, or null). Raises XPTY0004 for non-string atomic types like
    /// xs:integer, xs:boolean, etc.
    /// </summary>
    internal static void RequireStringArgument(object? arg, string paramName, bool allowEmpty = true)
    {
        if (arg is null && !allowEmpty)
            throw new XQueryRuntimeException("XPTY0004",
                $"Parameter ${paramName} expects xs:string, got empty sequence");
        if (arg is null or string or Xdm.XsUntypedAtomic or Xdm.XsAnyUri or Xdm.XsTypedString)
            return;
        throw new XQueryRuntimeException("XPTY0004",
            $"Parameter ${paramName} expects xs:string, got {arg.GetType().Name}");
    }
}
