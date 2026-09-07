using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:language($arg)</summary>
public sealed class LanguageConstructorFunction : TypeConstructorFunction
{
    public LanguageConstructorFunction() : base("language") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Only xs:string, xs:untypedAtomic, or xs:boolean can be cast to xs:language
        RequireStringOrUntyped(arg, "xs:language");
        var s = NormalizeWhitespace(AtomicToString(arg));
        if (s.Length == 0)
            throw new XQueryRuntimeException("FORG0001", "Empty string is not a valid xs:language");
        // Validate language tag per RFC 4646: [a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*
        if (!IsValidLanguage(s))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:language: '{s}'");
        return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsTypedString(s, "language"));
    }

    private static bool IsValidLanguage(string s)
    {
        var parts = s.Split('-');
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length < 1 || part.Length > 8) return false;
            for (int j = 0; j < part.Length; j++)
            {
                var c = part[j];
                if (i == 0)
                {
                    // First subtag: only letters
                    if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
                }
                else
                {
                    // Subsequent subtags: letters and digits
                    if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false;
                }
            }
        }
        return true;
    }
}
