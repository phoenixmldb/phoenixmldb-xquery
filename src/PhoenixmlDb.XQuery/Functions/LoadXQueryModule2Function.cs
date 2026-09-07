using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:load-xquery-module($module-uri, $options) as map(*)</summary>
public sealed class LoadXQueryModule2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "load-xquery-module");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "module-uri"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "options"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return LoadXQueryModuleHelper.LoadAsync(arguments[0], arguments[1] as System.Collections.IDictionary, context);
    }
}
