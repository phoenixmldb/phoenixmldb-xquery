using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// phoenixmldb-xquery#422: the xhtml output method is the XML method with the exceptions of
/// Serialization 3.1 §6. It wrote names without their prefixes (<c>xml:space</c> became
/// <c>space</c>, <c>p:a</c> and <c>a</c> two attributes named <c>a</c>, <c>p:span</c> a <c>span</c> in
/// the default namespace), dropped namespace declarations no name used, dropped comments and
/// processing instructions in <c>style</c> and <c>script</c>, wrote their text unescaped, wrote an
/// empty element in another namespace with a void HTML name as <c>&lt;img /&gt;</c>, and wrote
/// <c>&lt;!DOCTYPE html&gt;</c> before any element. Expected results: Saxon-HE 10.9's
/// <c>serialize()</c> for the same query, with indent off and html-version given. With html-version
/// 5.0 Saxon-HE writes a void element as <c>&lt;br/&gt;</c>; this serializer keeps the space
/// (<c>&lt;br /&gt;</c>), which the specification allows there and requires below 5.0.
/// </summary>
public partial class XhtmlSerializationTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("img-space v5", """serialize(<img xml:space="preserve"/>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<img xml:space="preserve"/>""")]
    [InlineData("img-space v1", """serialize(<img xml:space="preserve"/>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<img xml:space="preserve"></img>""")]
    [InlineData("pfx-attr v5", """serialize(<r xmlns:p="urn:p" p:a="1" a="2"/>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<r xmlns:p="urn:p" p:a="1" a="2"></r>""")]
    [InlineData("pfx-attr v1", """serialize(<r xmlns:p="urn:p" p:a="1" a="2"/>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<r xmlns:p="urn:p" p:a="1" a="2"></r>""")]
    [InlineData("pfx-elem v5", """serialize(<p:span xmlns:p="urn:p">t</p:span>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<p:span xmlns:p="urn:p">t</p:span>""")]
    [InlineData("pfx-elem v1", """serialize(<p:span xmlns:p="urn:p">t</p:span>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<p:span xmlns:p="urn:p">t</p:span>""")]
    [InlineData("unused-ns v5", """serialize(<r xmlns:q="urn:q"><s/></r>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<r xmlns:q="urn:q"><s></s></r>""")]
    [InlineData("unused-ns v1", """serialize(<r xmlns:q="urn:q"><s/></r>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<r xmlns:q="urn:q"><s></s></r>""")]
    [InlineData("style-cpi v5", """serialize(<style><!--c--><?p x?>s</style>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<style><!--c--><?p x?>s</style>""")]
    [InlineData("style-cpi v1", """serialize(<style><!--c--><?p x?>s</style>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<style><!--c--><?p x?>s</style>""")]
    [InlineData("script-c v5", """serialize(<script><!--c-->s</script>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<script><!--c-->s</script>""")]
    [InlineData("script-c v1", """serialize(<script><!--c-->s</script>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<script><!--c-->s</script>""")]
    [InlineData("script-esc v5", """serialize(<script>if (a &lt; b &amp;&amp; c) {{}}</script>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<script>if (a &lt; b &amp;&amp; c) {}</script>""")]
    [InlineData("script-esc v1", """serialize(<script>if (a &lt; b &amp;&amp; c) {{}}</script>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<script>if (a &lt; b &amp;&amp; c) {}</script>""")]
    [InlineData("style-esc v5", """serialize(<style a="x&lt;y">p &gt; i</style>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<style a="x&lt;y">p &gt; i</style>""")]
    [InlineData("style-esc v1", """serialize(<style a="x&lt;y">p &gt; i</style>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<style a="x&lt;y">p &gt; i</style>""")]
    [InlineData("foreign-img v5", """serialize(<p:img xmlns:p="urn:x"/>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<p:img xmlns:p="urn:x"></p:img>""")]
    [InlineData("foreign-img v1", """serialize(<p:img xmlns:p="urn:x"/>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<p:img xmlns:p="urn:x"></p:img>""")]
    [InlineData("html-root v5", """serialize(<html><body><br/><p/></body></html>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<!DOCTYPE html><html><body><br/><p></p></body></html>""")]
    [InlineData("html-root v1", """serialize(<html><body><br/><p/></body></html>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<html><body><br></br><p></p></body></html>""")]
    [InlineData("xhtml-root v5", """serialize(<html xmlns="http://www.w3.org/1999/xhtml"><body><br/><p/></body></html>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<!DOCTYPE html><html xmlns="http://www.w3.org/1999/xhtml"><body><br/><p></p></body></html>""")]
    [InlineData("xhtml-root v1", """serialize(<html xmlns="http://www.w3.org/1999/xhtml"><body><br/><p/></body></html>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<html xmlns="http://www.w3.org/1999/xhtml"><body><br /><p></p></body></html>""")]
    [InlineData("xhtml-pfx v5", """serialize(<h:html xmlns:h="http://www.w3.org/1999/xhtml"><h:br/></h:html>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<!DOCTYPE html><html xmlns="http://www.w3.org/1999/xhtml"><br/></html>""")]
    [InlineData("xhtml-pfx v1", """serialize(<h:html xmlns:h="http://www.w3.org/1999/xhtml"><h:br/></h:html>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<h:html xmlns:h="http://www.w3.org/1999/xhtml"><h:br /></h:html>""")]
    [InlineData("svg-pfx v5", """serialize(<s:svg xmlns:s="http://www.w3.org/2000/svg"><s:g/></s:svg>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<svg xmlns="http://www.w3.org/2000/svg"><g></g></svg>""")]
    [InlineData("svg-pfx v1", """serialize(<s:svg xmlns:s="http://www.w3.org/2000/svg"><s:g/></s:svg>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<s:svg xmlns:s="http://www.w3.org/2000/svg"><s:g></s:g></s:svg>""")]
    [InlineData("xhtml-attr-pfx v5", """serialize(<h:html xmlns:h="http://www.w3.org/1999/xhtml" h:lang="en"><h:br/></h:html>, map{"method":"xhtml","indent":false(),"html-version":5})""", """<!DOCTYPE html><html xmlns="http://www.w3.org/1999/xhtml" xmlns:h="http://www.w3.org/1999/xhtml" h:lang="en"><br/></html>""")]
    [InlineData("xhtml-attr-pfx v1", """serialize(<h:html xmlns:h="http://www.w3.org/1999/xhtml" h:lang="en"><h:br/></h:html>, map{"method":"xhtml","indent":false(),"html-version":1.0})""", """<h:html xmlns:h="http://www.w3.org/1999/xhtml" h:lang="en"><h:br /></h:html>""")]
    public async Task The_xhtml_method_writes_what_Saxon_writes(string label, string query, string saxon)
        // With html-version 5.0 a void element is written <br/>, as Saxon-HE writes it (ruled 2026-11-24);
        // below 5.0 <br />.
        => (await _facade.EvaluateAsync(query)).Should().Be(saxon, label);

    /// <summary>
    /// Without html-version, fn:serialize takes 5 (F&amp;O 3.1, the parameter table of
    /// fn:serialize). Saxon-HE 10.9 behaves as though it were absent (XHTML 1.0 rules) and indents.
    /// </summary>
    [Theory]
    [InlineData("""serialize(<html><body><br/><p/></body></html>, map{"method":"xhtml"})""", "<!DOCTYPE html><html><body><br/><p></p></body></html>")]
    [InlineData("""serialize(<img/>, map{"method":"xhtml"})""", "<img/>")]
    [InlineData("""serialize(<r><img/></r>, map{"method":"xhtml"})""", "<r><img/></r>")]
    [InlineData("""serialize(<h:p xmlns:h="http://www.w3.org/1999/xhtml"/>, map{"method":"xhtml"})""", """<p xmlns="http://www.w3.org/1999/xhtml"></p>""")]
    public async Task Without_html_version_the_xhtml_method_follows_HTML5(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);
}
