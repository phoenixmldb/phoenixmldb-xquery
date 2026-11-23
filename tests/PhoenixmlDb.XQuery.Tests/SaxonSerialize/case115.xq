declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<section v2="&quot;b&#10; &lt;x&quot;'z&lt;&#9;{{&#10;{{ xx&lt;' aé&amp;" id0="b&quot; &amp;a{{z&gt;éx &#10;xca&#10;cbéy c{{&#10;&quot;&lt;" n3="éx yx&amp;&#9;z&lt;y&amp;&amp;&gt;a" id3="&quot;">   </section>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
