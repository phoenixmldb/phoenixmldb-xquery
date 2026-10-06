using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Shared implementation of <c>fn:load-xquery-module</c> for the 1- and 2-arg forms.
/// Compiles the requested module in a sub-engine, executes its prolog declarations
/// to register functions and bind variables, and returns the spec-required result
/// map: <c>{"functions": map(xs:QName, map(xs:integer, function(*))), "variables": map(xs:QName, item()*)}</c>.
/// </summary>
internal static class LoadXQueryModuleHelper
{
    /// <summary>
    /// The 'variables' and 'vendor-options' options are map(xs:QName, item()*): anything else,
    /// a non-map or a map with a non-QName key, is XPTY0004.
    /// </summary>
    private static System.Collections.IDictionary RequireQNameKeyedMap(object? value, string option)
    {
        if (value is not System.Collections.IDictionary map)
            throw new XQueryRuntimeException("XPTY0004",
                $"The load-xquery-module option '{option}' must be a map(xs:QName, item()*)");
        foreach (System.Collections.DictionaryEntry e in map)
            if (e.Key is not QName)
                throw new XQueryRuntimeException("XPTY0004",
                    $"The load-xquery-module option '{option}' must have xs:QName keys; found {e.Key?.GetType().Name ?? "()"}");
        return map;
    }

    public static async ValueTask<object?> LoadAsync(
        object? moduleUriArg, System.Collections.IDictionary? optionsRaw, Ast.ExecutionContext context)
    {
        var moduleUri = moduleUriArg?.ToString() ?? "";
        if (string.IsNullOrEmpty(moduleUri))
            throw new XQueryRuntimeException("FOQM0001", "The module URI must not be a zero-length string");

        var locationHints = new List<string>();
        object? optContextItem = null;
        System.Collections.IDictionary? optVariables = null;

        if (optionsRaw != null)
        {
            foreach (System.Collections.DictionaryEntry entry in optionsRaw)
            {
                var key = entry.Key?.ToString() ?? "";
                switch (key)
                {
                    case "location-hints":
                        foreach (var s in CoerceToStringSeq(entry.Value))
                            if (!string.IsNullOrEmpty(s)) locationHints.Add(s);
                        break;
                    case "context-item":
                        if (entry.Value is object?[] { Length: > 1 } or List<object?> { Count: > 1 })
                            throw new XQueryRuntimeException("XPTY0004",
                                "The load-xquery-module option 'context-item' must be a single item or empty (item()?)");
                        optContextItem = entry.Value;
                        break;
                    case "variables":
                        optVariables = RequireQNameKeyedMap(entry.Value, "variables");
                        break;
                    case "vendor-options":
                        RequireQNameKeyedMap(entry.Value, "vendor-options");
                        break;
                    case "xquery-version":
                        // xs:decimal (F&O 3.1 §17.1.4); a string such as "3.1" is not one.
                        if (entry.Value is not (decimal or long or int or double or float))
                            throw new XQueryRuntimeException("XPTY0004",
                                "The load-xquery-module option 'xquery-version' must be an xs:decimal");
                        // The minimum version the processor must support. This engine supports up to
                        // 4.0 (a later `xquery version` declaration is XQST0031); asking for more is
                        // FOQM0006, no suitable processor.
                        if (Convert.ToDecimal(entry.Value, System.Globalization.CultureInfo.InvariantCulture) > 4.0m)
                            throw new XQueryRuntimeException("FOQM0006",
                                $"No XQuery processor supporting version {entry.Value} is available (this one supports up to 4.0)");
                        break;
                }
            }
        }

        var qec = context as Execution.QueryExecutionContext;
        var baseUri = qec?.StaticBaseUri;

        // Synthesize a tiny main query that imports the requested module so the
        // existing analyzer/optimizer/runtime do all the heavy lifting (module
        // resolution, function registration, variable initialization).
        var moduleUriEsc = moduleUri.Replace("\"", "\"\"", StringComparison.Ordinal);
        string importStmt;
        if (locationHints.Count > 0)
        {
            var hintList = string.Join(", ",
                locationHints.Select(h => "\"" + h.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
            importStmt = $"import module namespace __lxqm = \"{moduleUriEsc}\" at {hintList}; ()";
        }
        else
        {
            importStmt = $"import module namespace __lxqm = \"{moduleUriEsc}\"; ()";
        }

        // The loaded module runs under the caller's resource policy: its location hints, its own
        // imports and everything it reads are checked exactly as the calling query's would be.
        // It also builds and reads nodes in the caller's store: an element the module constructs
        // is returned to, and navigated by, the calling query. An engine created without a node
        // provider had nowhere to build them, so every module function containing an element
        // constructor failed with "requires a node store implementing INodeBuilder".
        var subEngine = new Execution.QueryEngine(
            nodeProvider: qec?.NodeProvider,
            documentResolver: qec?.DocumentResolver,
            schemaProvider: qec?.SchemaProvider) { ResourcePolicy = context.ResourcePolicy };
        var compResult = subEngine.Compile(importStmt, new Execution.CompilationOptions
        {
            BaseUri = baseUri,
            // The host's module maps, as the calling query's own `import module` sees them.
            ExternalModules = qec?.ExternalModules,
            ExternalModuleLocations = qec?.ExternalModuleLocations,
        });

        if (!compResult.Success)
        {
            var msg = string.Join("; ", compResult.Errors.Select(e => $"[{e.Code}] {e.Message}"));
            // F&O 3.1 §17.1.4 names the failure: FOQM0002 when no module for the URI can be
            // found, FOQM0003 when the module has a static error. The analyzer's own code (XQST0059,
            // XPST0003, …) is the precise diagnosis and stays in the message.
            // A module that fails to compile is also reported as unresolved (XQST0059) because it
            // was never registered; that consequence must not hide the static error that caused it.
            var notFound = compResult.Errors.All(e => e.Code == "XQST0059");
            throw new XQueryRuntimeException(notFound ? "FOQM0002" : "FOQM0003",
                $"Module '{moduleUri}' cannot be loaded: {msg}");
        }
        var staticCtx = compResult.StaticContext;
        if (staticCtx == null || !staticCtx.ImportedModules.TryGetValue(moduleUri, out var moduleExpr))
            throw new XQueryRuntimeException("FOQM0002",
                $"Module '{moduleUri}' was not registered as an imported module after compilation");

        // Run the trivial query body — this fires the function/variable declaration
        // operators that register DeclaredFunctions in the sub-engine's function library
        // and bind global variables in the sub-execution context.
        // The returned function items capture this sub-context (closure variables, static
        // base URI). Disposing it would invalidate every function in the result map, so
        // the lifetime intentionally outlives this scope — suppress CA2000 here.
#pragma warning disable CA2000
        var subContext = subEngine.CreateContext(initialContextItem: optContextItem);
#pragma warning restore CA2000
        if (optVariables != null)
        {
            foreach (System.Collections.DictionaryEntry e in optVariables)
            {
                if (e.Key is QName qn)
                    subContext.SetExternalVariable(qn, e.Value);
            }
        }
        try
        {
            await foreach (var _ in compResult.ExecutionPlan!.ExecuteAsync(subContext)) { /* drain */ }
        }
        catch (XQueryRuntimeException ex) when (ex.ErrorCode == "XPTY0004"
            && (optVariables != null || optContextItem != null)
            && ex.Message.Contains("does not match declared type", StringComparison.Ordinal))
        {
            // A supplied external variable or context item that does not match the type the
            // module declares for it is FOQM0005 (F&O 3.1 §17.1.4), not the raw type error.
            throw new XQueryRuntimeException("FOQM0005",
                "A value supplied to load-xquery-module does not match the type the module declares: " + ex.Message, ex);
        }

        // Build the functions map: QName → map(xs:integer arity → function-item).
        // Private functions and variables (declared %private) are not exposed.
        // Use the XDM map-key comparer so QName lookups via fn:QName() (which mints
        // RuntimeNamespace-only QNames) match the QName objects we use as keys here.
        var functionsMap = new Execution.OrderedXdmMap(Execution.XdmMapKeyComparer.Instance);
        var variablesMap = new Execution.OrderedXdmMap(Execution.XdmMapKeyComparer.Instance);

        foreach (var decl in moduleExpr.Declarations)
        {
            if (decl is Ast.FunctionDeclarationExpression fd && !fd.IsPrivate)
            {
                var func = subContext.Functions.Resolve(fd.Name, fd.Parameters.Count);
                if (func == null) continue;

                if (!functionsMap.TryGetValue(fd.Name, out var arityMapObj)
                    || arityMapObj is not IDictionary<object, object?> arityMap)
                {
                    arityMap = new Execution.OrderedXdmMap(Execution.XdmMapKeyComparer.Instance);
                    functionsMap[fd.Name] = arityMap;
                }
                arityMap[(long)fd.Parameters.Count] = func;
            }
            else if (decl is Ast.VariableDeclarationExpression vd && !vd.IsPrivate)
            {
                try { variablesMap[vd.Name] = subContext.GetVariable(vd.Name); }
                catch { /* unbound external — skip */ }
            }
        }

        return new Execution.OrderedXdmMap(Execution.XdmMapKeyComparer.Instance)
        {
            ["functions"] = functionsMap,
            ["variables"] = variablesMap
        };
    }

    private static IEnumerable<string> CoerceToStringSeq(object? value)
    {
        switch (value)
        {
            case null:
                yield break;
            case string s:
                yield return s;
                yield break;
            case object?[] arr:
                foreach (var x in arr) yield return x?.ToString() ?? "";
                yield break;
            case System.Collections.IEnumerable seq:
                foreach (var x in seq) yield return x?.ToString() ?? "";
                yield break;
            default:
                yield return value.ToString() ?? "";
                yield break;
        }
    }
}
