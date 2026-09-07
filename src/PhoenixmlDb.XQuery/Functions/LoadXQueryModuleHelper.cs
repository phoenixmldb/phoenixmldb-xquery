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
                        optContextItem = entry.Value;
                        break;
                    case "variables":
                        optVariables = entry.Value as System.Collections.IDictionary;
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

        var subEngine = new Execution.QueryEngine();
        var compResult = subEngine.Compile(importStmt, new Execution.CompilationOptions
        {
            BaseUri = baseUri
        });

        if (!compResult.Success)
        {
            var msg = string.Join("; ", compResult.Errors.Select(e => e.Message));
            // Preserve the underlying static error code instead of flattening every
            // compilation failure to FOQM0002. The analyzer already classifies these
            // precisely — an unresolvable module namespace is XQST0059 — and that code is
            // the one piece of the diagnosis a caller can act on. Collapsing it lost the
            // distinction between "no such module", "the module has a syntax error", and
            // "the module imports something missing", reporting all three identically.
            var code = compResult.Errors
                .FirstOrDefault(e => !string.IsNullOrEmpty(e.Code))?.Code ?? "FOQM0002";
            throw new XQueryRuntimeException(code,
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
        await foreach (var _ in compResult.ExecutionPlan!.ExecuteAsync(subContext)) { /* drain */ }

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
