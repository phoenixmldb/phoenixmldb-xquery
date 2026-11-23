declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<section xmlns:long-prefix="urn:example:a-much-longer-namespace-uri"><data-element xmlns:p="urn:p">{{&gt;"<section v2="z ab x&#10; x&#10;&quot;&amp;}}&#10;&lt;x"></section></data-element></section>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
