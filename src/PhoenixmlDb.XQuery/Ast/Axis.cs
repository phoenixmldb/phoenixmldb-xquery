using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// XPath axis specifier.
/// </summary>
public enum Axis
{
    Child,
    Descendant,
    Attribute,
    Self,
    DescendantOrSelf,
    FollowingSibling,
    Following,
    Parent,
    Ancestor,
    PrecedingSibling,
    Preceding,
    AncestorOrSelf,
    Namespace
}
