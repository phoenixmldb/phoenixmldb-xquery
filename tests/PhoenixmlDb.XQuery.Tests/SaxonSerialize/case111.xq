declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<item n1="ax }}cb&gt;zz bc&quot;b&quot;é{{&quot;xaé&#10;''y&#9;&#9;&amp;"></item>, "<
b{", "é{}
"), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "item-separator": "|" })
