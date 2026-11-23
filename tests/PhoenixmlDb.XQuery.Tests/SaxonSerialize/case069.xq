declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<em xmlns="http://www.w3.org/1999/xhtml" n3="&lt;" v2="&amp;&#9;&gt;&#10;&lt;y&amp;&lt;éc'a &amp;}}é &lt;&#9;'{{&quot;c"></em>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
