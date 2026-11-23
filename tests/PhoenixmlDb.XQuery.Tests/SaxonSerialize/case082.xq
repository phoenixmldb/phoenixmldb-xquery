declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <e n0="&lt;&quot;}}b&quot;a&lt;&amp;&#10;&gt;éy"><!--b>
--><x v0="}} &quot;{{ yb'&amp;&#10;c&lt;aza{{bxbc&quot;&#10;xz " type1="z&quot;&lt;bé'a&quot;&quot;z}}a &gt;"></x></e> }), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "doctype-system": "case.dtd" })
