declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<b n1="xa&amp; xa&lt;}}aéc&amp;&#10;&quot;bc}}&amp;a&#10;'" a-long-attribute-name2="&quot;z&amp;}}z}}a" xml:space="preserve">}}	</b>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "doctype-system": "case.dtd" })
