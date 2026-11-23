declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <a a-long-attribute-name1="'&gt;'{{x" n2="&amp; é&gt;z&#9;&lt;}}b{{éé&quot;yx&#9;{{&quot;bzb&gt;'"></a> }), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
