using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// XQuery §4.4 — the copy-namespaces declaration applies to element constructors in the
/// module where it is declared. Library modules that do not declare copy-namespaces use
/// the default (preserve, inherit), regardless of the importing query's declaration.
///
/// Regression test for QT3 nscons-036: the importing query declares preserve/no-inherit,
/// but the library module uses preserve/inherit (default). Inner elements produced inside
/// the library module's constructors must NOT receive the importer's no-inherit sentinel.
/// </summary>
public sealed class ModuleCopyNamespacesScopingTests
{
    [Fact]
    public async System.Threading.Tasks.Task LibraryModule_UsesOwnCopyNsMode_NotImporterMode()
    {
        // QT3 nscons-036: importer declares preserve/no-inherit; module uses default
        // (preserve/inherit). When $elem/outer/inner is returned, inner must carry
        // xmlns:out from the ancestor outer (preserve) but NOT xmlns:new from element e
        // (no-inherit prevents inheritance from e's context).
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "xq-copyns-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        var modPath = System.IO.Path.Combine(tempDir, "cnc-module.xq").Replace("\\", "/");
        await System.IO.File.WriteAllTextAsync(modPath,
            """
            module namespace mod1 = "http://www.w3.org/TestModules/cnc-module";

            declare function mod1:nested() as element() {
              element outer {
                namespace out { "http://out.zorba-xquery.com/" },
                element inner {
                  namespace in { "http://in.zorba-xquery.com/" }
                }
              }
            };
            """);

        var query = $$"""
            import module namespace mod1="http://www.w3.org/TestModules/cnc-module" at "{{modPath}}";
            declare copy-namespaces preserve, no-inherit;
            let $nested := mod1:nested(),
                $elem := element e { namespace new { "http://new.zorba-xquery.com/" }, $nested }
            return
              $elem/outer/inner
            """;

        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(
            string.Join("; ", compiled.Errors.Select(e => e.Message)));

        using var ctx = engine.CreateContext();
        var items = new System.Collections.Generic.List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            items.Add(item);

        items.Should().ContainSingle("the result should be a single element");
        var inner = items[0] as PhoenixmlDb.Xdm.Nodes.XdmElement;
        inner.Should().NotBeNull("the result should be an XdmElement");

        // inner must carry its own xmlns:in AND xmlns:out (preserved from ancestor outer),
        // but NOT xmlns:new (no-inherit prevents inheriting from element e's context).
        // The inner element's declarations should be [xmlns:in] only; the serializer
        // picks up xmlns:out from the ancestor chain (outer element has it, with sentinel).
        var innerDecls = inner!.NamespaceDeclarations ?? [];
        // inner itself should only have xmlns:in (not the sentinel — module used default mode)
        var innerPrefixes = innerDecls.Select(nd => nd.Prefix).ToList();
        const string sentinel = "no-inherit";
        innerPrefixes.Should().NotContain(sentinel,
            "the no-inherit sentinel must NOT be on inner — it belongs only on the copy root (outer)");
        innerPrefixes.Should().Contain("in",
            "inner's own xmlns:in must be present");

        // The serialized XML (via the ancestor walk that the conformance test runner uses)
        // should produce: <inner xmlns:in="..." xmlns:out="..."/>
        // Verify the ancestor chain: outer (with sentinel) is inner's parent in the store.
        inner.Parent.HasValue.Should().BeTrue("inner must have a parent in the constructed tree");
        var outerNode = inner.Parent.HasValue
            ? store.GetNode(inner.Parent.Value) as PhoenixmlDb.Xdm.Nodes.XdmElement
            : null;
        outerNode.Should().NotBeNull("inner's parent must be the outer element");
        outerNode!.LocalName.Should().Be("outer");

        var outerDecls = outerNode.NamespaceDeclarations ?? [];
        var outerPrefixes = outerDecls.Select(nd => nd.Prefix).ToList();
        outerPrefixes.Should().Contain("out",
            "outer must carry xmlns:out (preserve mode preserved it)");
        outerPrefixes.Should().Contain(sentinel,
            "outer must carry the no-inherit sentinel (copy root gets it from element e)");

        try { System.IO.Directory.Delete(tempDir, recursive: true); } catch (System.IO.IOException) { }
    }
}
