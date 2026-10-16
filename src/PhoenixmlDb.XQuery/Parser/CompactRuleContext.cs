using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace PhoenixmlDb.XQuery.Parser;

/// <summary>
/// The base of every parse-tree node of the XQuery grammar (its <c>contextSuperClass</c>). A
/// node's children were a <see cref="List{T}"/> whose first child made an array of four, and
/// most nodes have one child: the grammar's precedence chain gives a literal some twenty
/// single-child nodes. Those arrays were a large part of the allocation of a stylesheet compile
/// (#401). Here the list starts with room for one child, and grows as a list does. It stays a
/// <see cref="List{T}"/>, so code that enumerates the children keeps the list's fast paths.
/// </summary>
public class CompactRuleContext : ParserRuleContext
{
    public CompactRuleContext()
    {
    }

    public CompactRuleContext(ParserRuleContext parent, int invokingStateNumber)
        : base(parent, invokingStateNumber)
    {
    }

    public override void AddChild(ITerminalNode t)
    {
        children ??= new List<IParseTree>(1);
        children.Add(t);
    }

    public override void AddChild(RuleContext ruleInvocation)
    {
        children ??= new List<IParseTree>(1);
        children.Add(ruleInvocation);
    }
}
