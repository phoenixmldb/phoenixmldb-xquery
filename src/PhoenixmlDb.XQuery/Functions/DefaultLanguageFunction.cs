using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:default-language() as xs:language — returns system default language (XPath 4.0).
/// </summary>
public sealed class DefaultLanguageFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "default-language");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var lang = (context as Execution.QueryExecutionContext)?.DefaultLanguage
            ?? System.Globalization.CultureInfo.CurrentCulture.Name;
        return ValueTask.FromResult<object?>(lang);
    }
}
