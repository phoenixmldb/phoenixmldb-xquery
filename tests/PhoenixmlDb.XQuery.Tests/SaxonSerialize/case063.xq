declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<long-prefix:item xmlns:long-prefix="urn:example:a-much-longer-namespace-uri" n1="é&quot;&#9;&amp;x{{&lt; c&#10;&#10;{{é&amp;" id0="y&amp;ay" id1="&lt;yab{{{{&lt;{{a&amp;&lt;}}''xy&quot;&lt;a}}" v2="&amp;xcb&quot;é}}a&quot;é '}}}}c&lt;&lt;&lt;y &#9;{{z" n2="éc&lt;&gt; " long-prefix:a-long-attribute-name0=""></long-prefix:item>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false() })
