using System.Collections;
using System.Reflection;
using AwesomeAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// QueryExecutionContext.ResetForReuse returns a used context to the state of a new one, so a host
/// can evaluate many expressions with one context (xquery#294). Compared field by field, by
/// reflection: a field added to the context later and not reset there fails this test.
/// </summary>
public class ContextResetTests
{
    private static QueryExecutionContext NewContext() => new(default);

    [Fact]
    public void A_reset_context_equals_a_new_one_in_every_field()
    {
        var used = NewContext();
        var name = new QName(NamespaceId.None, "v");
        used.PushScope();
        used.BindVariable(name, 1L);
        used.PushContextItem("item", 2, 3);
        used.SetExternalVariable(name, "x");
        used.EnterFunctionCall();
        used.BackwardsCompatible = true;
        used.InsideXslEvaluate = true;
        used.DefaultCollation = "c";
        used.DefaultLanguage = "de";
        used.StaticBaseUri = "urn:base";
        used.ModuleLocation = "urn:module";
        used.CurrentModuleNamespace = "urn:ns";
        used.BoundarySpaceStrip = true;
        used.CopyNamespacesMode = PhoenixmlDb.XQuery.Analysis.CopyNamespacesMode.NoPreserveNoInherit;
        used.ConstructionMode = PhoenixmlDb.XQuery.Analysis.ConstructionMode.Strip;
        used.PrefixNamespaceBindings = new Dictionary<string, string> { ["p"] = "urn:p" };
        used.PrologNamespaceBindings = new Dictionary<string, string> { ["p"] = "urn:p" };
        used.EnclosingConstructorBindings = new Dictionary<string, string> { ["p"] = "urn:p" };
        used.VariableFallback = _ => (false, null);
        used.ImplicitTimezone = TimeSpan.FromHours(2);
        _ = used.CurrentDateTime;
        _ = used.DecimalFormats;
        _ = used.PendingUpdates;
        using (used.PushLocation(new PhoenixmlDb.XQuery.Ast.SourceLocation(1, 2, 0, 0))) { }
        using var location = used.PushLocation(new PhoenixmlDb.XQuery.Ast.SourceLocation(3, 4, 0, 0));

        used.ResetForReuse();

        var fresh = NewContext();
        foreach (var field in typeof(QueryExecutionContext).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            var a = field.GetValue(used);
            var b = field.GetValue(fresh);
            Describe(a).Should().Be(Describe(b), $"field {field.Name} must be reset");
        }
    }

    [Fact]
    public void A_captured_context_is_not_reset()
    {
        var captured = NewContext();
        captured.Captured = true;
        var act = () => captured.ResetForReuse();
        act.Should().Throw<InvalidOperationException>();
    }

    // A value as a comparable shape: null, a collection's count, a value type or string as itself,
    // and for any other object its type (the readonly references are the same object or both fresh).
    private static string Describe(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        ICollection c => $"{value.GetType().Name}[{c.Count}]",
        Delegate => "delegate",
        _ when value.GetType().IsValueType => value.ToString() ?? "",
        _ => value.GetType().Name,
    };
}
