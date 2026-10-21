using System.Xml;
using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// The descendant and descendant-or-self axes walk a stack of child lists instead of one child
/// iterator per node (xquery#294). Their nodes and order are checked against System.Xml's own
/// document-order walk of the same text, an independent method.
/// </summary>
public class DescendantWalkTests
{
    private const string Doc = """
        <r a="1"><x><y>t1<!--c--><z/></y>t2</x><?p d?><x><y/><y><z>t3</z></y></x>tail</r>
        """;

    // Each node as kind and name or text, in document order, as System.Xml walks it.
    private static string Expected(bool includeSelf)
    {
        var doc = new XmlDocument();
        doc.LoadXml(Doc);
        var root = doc.DocumentElement!;
        var parts = new List<string>();
        void Walk(XmlNode n)
        {
            foreach (XmlNode c in n.ChildNodes)
            {
                parts.Add(Describe(c));
                Walk(c);
            }
        }
        if (includeSelf)
            parts.Add(Describe(root));
        Walk(root);
        return string.Join(",", parts);
    }

    private static string Describe(XmlNode n) => n.NodeType switch
    {
        XmlNodeType.Element => "e:" + n.Name,
        XmlNodeType.Text => "t:" + n.Value,
        XmlNodeType.Comment => "c:" + n.Value,
        XmlNodeType.ProcessingInstruction => "p:" + n.Name,
        _ => "?",
    };

    private const string DescribeXq = """
        declare function local:d($n) {
          typeswitch ($n)
            case element() return 'e:' || name($n)
            case text() return 't:' || string($n)
            case comment() return 'c:' || string($n)
            case processing-instruction() return 'p:' || name($n)
            default return '?'
        };
        """;

    [Theory]
    [InlineData("descendant", false)]
    [InlineData("descendant-or-self", true)]
    public async Task The_nodes_are_those_of_a_document_order_walk(string axis, bool includeSelf)
        => (await new XQueryFacade().EvaluateAsync(DescribeXq + $"string-join(/r/{axis}::node() ! local:d(.), ',')", Doc))
            .Should().Be(Expected(includeSelf));
}
