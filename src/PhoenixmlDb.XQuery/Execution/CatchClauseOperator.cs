using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Catch clause operator.
/// </summary>
public sealed class CatchClauseOperator
{
    public required IReadOnlyList<NameTest> ErrorCodes { get; init; }
    public required PhysicalOperator ResultOperator { get; init; }

    public bool Matches(string errorCode) => Matches(errorCode, null);

    public bool Matches(string errorCode, string? errorNamespaceUri)
    {
        // Error codes like "FOAR0001" live in the err: namespace by default.
        // User-raised errors via fn:error(xs:QName) carry an explicit namespace.
        const string ErrNs = "http://www.w3.org/2005/xqt-errors";
        var actualNs = errorNamespaceUri ?? ErrNs;
        foreach (var test in ErrorCodes)
        {
            bool localMatch = test.LocalName == "*" || test.LocalName == errorCode;

            bool nsMatch;
            if (test.Prefix == null && test.NamespaceUri == null && test.LocalName == "*" && !test.IsNamespaceWildcard)
                nsMatch = true;                                       // catch *
            else if (test.IsNamespaceWildcard || test.NamespaceUri == "*")
                nsMatch = true;                                       // catch *:local
            else if (test.NamespaceUri != null)
                nsMatch = test.NamespaceUri == actualNs;              // catch Q{uri}...
            else if (test.Prefix != null)
                nsMatch = GetNamespaceForPrefix(test.Prefix) == actualNs;
            else
                nsMatch = actualNs == ErrNs;                          // unprefixed → err namespace

            if (localMatch && nsMatch)
                return true;
        }
        return false;
    }

    private static string? GetNamespaceForPrefix(string? prefix)
    {
        return prefix switch
        {
            "err" => "http://www.w3.org/2005/xqt-errors",
            "xs" => "http://www.w3.org/2001/XMLSchema",
            "fn" => "http://www.w3.org/2005/xpath-functions",
            "local" => "http://www.w3.org/2005/xquery-local-functions",
            "map" => "http://www.w3.org/2005/xpath-functions/map",
            "array" => "http://www.w3.org/2005/xpath-functions/array",
            _ => null
        };
    }
}
