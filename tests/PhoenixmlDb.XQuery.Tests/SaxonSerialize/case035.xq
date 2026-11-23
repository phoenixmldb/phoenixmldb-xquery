declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<e a-long-attribute-name2="&#10;&quot;zab&gt;}}bb&quot;&#10;y{{&lt;a&#9;&lt;'&amp;{{ " n1="&amp;" id1="&quot;&#10;&gt;"><!--""
>--><!--}b--><!-- a
	"" yy'-->{{{{ba&amp;&amp;c</e>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
