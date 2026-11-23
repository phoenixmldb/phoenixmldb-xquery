declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<b></b>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
