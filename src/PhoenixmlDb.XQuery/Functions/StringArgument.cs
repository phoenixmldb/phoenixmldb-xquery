namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// The string value of an argument declared xs:string, under the function conversion rules.
/// </summary>
/// <remarks>
/// An xs:string parameter accepts xs:string and its subtypes (xs:NCName, xs:language, … — an
/// <see cref="Xdm.XsTypedString"/>), xs:untypedAtomic, and xs:anyURI, which is PROMOTED to
/// xs:string. Built-ins each unwrapped their own subset, and several accepted only
/// string/untypedAtomic: fn:string-to-codepoints(namespace-uri()) raised XPTY0004 the moment
/// fn:namespace-uri returned the xs:anyURI the spec requires (xquery#83). One rule, one place.
/// </remarks>
internal static class StringArgument
{
    /// <summary>The value as a CLR string, or null when it is not in the string family.</summary>
    public static string? AsString(object? value) => value switch
    {
        string s => s,
        Xdm.XsUntypedAtomic u => u.Value,
        Xdm.XsTypedString t => t.Value,
        Xdm.XsAnyUri a => a.Value,
        _ => null,
    };
}
