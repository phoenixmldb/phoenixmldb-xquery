using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:load-xquery-module($module-uri as xs:string) as map(*)</summary>
public sealed class LoadXQueryModuleFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "load-xquery-module");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "module-uri"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return LoadXQueryModuleHelper.LoadAsync(arguments[0], optionsRaw: null, context);
    }
}
