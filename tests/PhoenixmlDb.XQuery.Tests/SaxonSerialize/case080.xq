declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<data-element xmlns:q="urn:example:q" v3="béa'&#10;é" q:a-long-attribute-name1="{{}}{{'&quot;&amp;bz{{'{{yy&amp;{{yzx"><!--cx-->'ébx
bcc&lt;"}}{{</data-element>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "suppress-indentation": (QName("", "data-element")) })
