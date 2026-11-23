declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<item xmlns:long-prefix="urn:example:a-much-longer-namespace-uri" type3="&amp;a &gt; &#9;z&#10;&quot;cé'bca&lt;é&lt;a'b&amp;{{&quot;&gt;'&amp;"></item>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "doctype-system": "case.dtd", "doctype-public": "-//PHX//CASE//EN" })
