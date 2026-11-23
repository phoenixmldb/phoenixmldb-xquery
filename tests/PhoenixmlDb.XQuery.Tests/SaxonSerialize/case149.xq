declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<em xmlns="http://www.w3.org/1999/xhtml" n0="}}}}y&lt;'&quot;&amp;'c'&quot;&lt;yz&#10;aa " n3="bb&lt;béx}}&amp;zxc}}&#10;xy"></em>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
