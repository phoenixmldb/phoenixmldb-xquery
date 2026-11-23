declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<P xmlns="http://www.w3.org/1999/xhtml" n1="}}z&#10;&amp;c&lt; x{{}}&#10;x " a-long-attribute-name3="&gt;}}'&quot;&lt;&#9;x}}&#9;z&#10;a}} &#10;" n2="c  zy z&quot;b bc&amp;&gt;&quot;&gt;y&#9;}}&amp;c&quot;&#10;c" a-long-attribute-name1="" v1="a&#9;aé{{z'}}&amp;&#9;z&quot;&#9;x&gt;&gt; " v2="z&quot;bé &quot;a">{{é<?pi1 >?></P>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
