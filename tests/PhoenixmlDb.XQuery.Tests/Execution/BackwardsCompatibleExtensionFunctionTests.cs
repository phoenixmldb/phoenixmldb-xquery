using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// In XSLT 1.0 backwards-compatible mode an unavailable EXTENSION function is the dynamic error
/// XTDE1425 when the call is evaluated; outside the mode, or in a standard namespace, XPST0017.
/// Driven at the operator: an XQuery query rejects an unknown function at compile time, and XSLT
/// is what reaches this run-time path.
/// </summary>
public sealed class BackwardsCompatibleExtensionFunctionTests
{
    private static async Task<string?> ErrorCodeAsync(NamespaceId ns, bool backwardsCompatible)
    {
        var call = new FunctionCallOperator
        {
            FunctionName = new QName(ns, "f"),
            ArgumentOperators = [new ConstantOperator { Value = 3L }],
        };
        using var context = new QueryExecutionContext(default, functions: FunctionLibrary.Standard)
        {
            BackwardsCompatible = backwardsCompatible,
        };
        try
        {
            await foreach (var _ in call.ExecuteAsync(context)) { }
            return null;
        }
        catch (XQueryRuntimeException e)
        {
            return e.ErrorCode;
        }
    }

    private static readonly NamespaceId Extension = new(9_999);

    [Fact]
    public async Task An_unavailable_extension_function_is_XTDE1425_in_the_mode()
        => (await ErrorCodeAsync(Extension, backwardsCompatible: true)).Should().Be("XTDE1425");

    [Fact]
    public async Task Outside_the_mode_it_is_XPST0017()
        => (await ErrorCodeAsync(Extension, backwardsCompatible: false)).Should().Be("XPST0017");

    [Fact]
    public async Task A_standard_namespace_is_XPST0017_even_in_the_mode()
        => (await ErrorCodeAsync(FunctionNamespaces.Fn, backwardsCompatible: true)).Should().Be("XPST0017");
}
