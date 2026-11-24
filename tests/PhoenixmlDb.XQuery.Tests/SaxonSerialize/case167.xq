declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<x a-long-attribute-name0="&lt;xz&gt;" type0="éa&amp;&#9;&#10;" type2="c{{azé&#10;&#9;c&quot;x&gt;'b&lt; czb&gt;b}}cx&#9;&#9;"><?pi1 	?><b id1="b&#10;&gt;éy&#9;x&#10;{{x'&lt;&amp;y&#9;&quot;&#9;a&amp;&amp;&quot;c&#9;'y {{yx"><?pi0 y?></b></x>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "html-version": 4.0 })
