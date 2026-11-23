declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <e a-long-attribute-name2="c&amp;a&#10;&#9;c&quot;'&#9;y&#9;&quot;}}'xxzyc&#10;cb{{é" v3="&amp;&#9;'" v0="zxbc&gt;&amp;x&amp;&gt; &quot;&gt; c}}&#9;yb'&gt;&quot;&lt;y&amp;{{" a-long-attribute-name3="é&gt;xaz&quot;z  éyé&quot;&gt;&#10;">{{&amp;ac{{"z</e> }, "	<é>"), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
