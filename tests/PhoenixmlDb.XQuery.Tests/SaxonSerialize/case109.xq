declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<x>zxéxbxzé<data-element xml:space="preserve"><?pi2 	zx	?><!--z{}é--><q:e xmlns:q="urn:example:q" q:n2="}}é&gt;y{{xa&lt;}}" q:a-long-attribute-name2="&#10;'&lt;{{{{" v3="yz }}&lt;&gt;&amp;&#10;y&lt;&amp;&gt;z&gt;&#10;&#10;z&gt;"><!----></q:e></data-element><!--z--></x>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
