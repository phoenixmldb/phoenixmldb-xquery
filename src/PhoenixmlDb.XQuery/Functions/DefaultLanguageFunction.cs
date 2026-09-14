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
        var lang = (context as Execution.QueryExecutionContext)?.DefaultLanguage;
        if (string.IsNullOrEmpty(lang))
            lang = System.Globalization.CultureInfo.CurrentCulture.Name;
        // The invariant culture's Name is the EMPTY STRING, and xs:language does not admit it —
        // its lexical space requires at least one alphabetic subtag. A host running under
        // DOTNET_SYSTEM_GLOBALIZATION_INVARIANT, or any culture-less container, therefore
        // returned a value that is not of the type this function declares. Fall back to a real
        // language tag rather than hand back something untypable (W3C QT3 default-language-001,
        // which fails on CI and passes on a developer machine purely because the two have
        // different locales).
        if (string.IsNullOrEmpty(lang))
            lang = "en";
        return ValueTask.FromResult<object?>(lang);
    }
}
