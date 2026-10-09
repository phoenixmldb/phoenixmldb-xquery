using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// A call whose arity no function declares exactly is given to a variadic function of the name
/// that takes that many arguments. The library finds it by name; it used to walk every function
/// it holds, for every name that is not a function at all.
/// </summary>
public sealed class FunctionLibraryVariadicLookupTests
{
    private static readonly QName Concat = new(FunctionNamespaces.Fn, "concat");

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(17)]
    public void A_variadic_function_is_found_at_any_arity_it_takes(int arity)
        => FunctionLibrary.Standard.Resolve(Concat, arity).Should().BeOfType<ConcatFunction>();

    [Fact]
    public void An_arity_it_does_not_take_finds_nothing()
    {
        var concat = FunctionLibrary.Standard.Resolve(Concat, 2)!;

        FunctionLibrary.Standard.Resolve(Concat, concat.MinArity - 1).Should().BeNull();
    }

    [Fact]
    public void A_name_that_is_no_function_finds_nothing()
    {
        FunctionLibrary.Standard.Resolve(new QName(FunctionNamespaces.Fn, "no-such-function"), 3).Should().BeNull();
        FunctionLibrary.Standard.Resolve(new QName(new NamespaceId(4242), "concat"), 3).Should().BeNull();
    }

    [Fact]
    public void A_copy_finds_the_same_and_keeps_its_own_registrations()
    {
        var original = FunctionLibrary.Standard.Copy();
        var copy = original.Copy();
        var replacement = new ConcatFunction();

        copy.Register(replacement);

        copy.Resolve(Concat, 5).Should().BeSameAs(replacement);
        original.Resolve(Concat, 5).Should().NotBeSameAs(replacement).And.BeOfType<ConcatFunction>();
    }
}
