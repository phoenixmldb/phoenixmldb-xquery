using FluentAssertions;
using PhoenixmlDb.Xdm.Nodes;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// This suite runs with Core's StrictStringValue ON (runtimeconfig.template.json), so a read of a
/// string value that was never computed fails a test instead of silently answering "". A strict
/// mode nobody enables catches nothing — and a misspelled switch name would leave it off without a
/// sound, which is what this guards.
/// </summary>
public class StrictStringValueGuardTests
{
    [Fact]
    public void Strict_string_value_is_enabled_for_this_suite()
        => XdmNode.StrictStringValue.Should().BeTrue(
            $"runtimeconfig.template.json must set \"{XdmNode.StrictStringValueSwitchName}\": true");
}
