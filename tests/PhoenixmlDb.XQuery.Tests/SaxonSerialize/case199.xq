declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<html xmlns="http://www.w3.org/1999/xhtml"><section xmlns="http://www.w3.org/1999/xhtml" id2="{{{{" n2="a&gt;&amp;{{x&lt;&lt;'&amp;y&quot;&lt;bb'&#9;xac&#10; é'éc}}" n3="{{{{y'&quot;éz&lt;&gt;x}}&gt;yb&#9;yc&#10;&#9;&#9;bx">
 c
<?pi0 "{zé?></section></html>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "html-version": 5 })
