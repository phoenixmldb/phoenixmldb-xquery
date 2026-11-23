declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<section xmlns:long-prefix="urn:example:a-much-longer-namespace-uri" a-long-attribute-name0="&#10;zb&quot;&#10;" n2="&quot;bc&lt;&amp;{{ aé{{zac &#9;&#10;yé&gt;&#10;&gt;a b'&#10;&quot;y"><?pi0 aa{y{&a?><?pi0 é
< ca?></section>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "suppress-indentation": (QName("", "section")) })
