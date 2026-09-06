using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Well-known namespace URIs.
/// </summary>
public static class WellKnownNamespaces
{
    public const string XmlUri = "http://www.w3.org/XML/1998/namespace";
    public const string XsUri = "http://www.w3.org/2001/XMLSchema";
    public const string XsiUri = "http://www.w3.org/2001/XMLSchema-instance";
    public const string FnUri = "http://www.w3.org/2005/xpath-functions";
    public const string LocalUri = "http://www.w3.org/2005/xquery-local-functions";
    public const string MapUri = "http://www.w3.org/2005/xpath-functions/map";
    public const string ArrayUri = "http://www.w3.org/2005/xpath-functions/array";
    public const string MathUri = "http://www.w3.org/2005/xpath-functions/math";
    public const string ErrUri = "http://www.w3.org/2005/xqt-errors";
}
