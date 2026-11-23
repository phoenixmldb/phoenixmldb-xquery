declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<title xmlns="http://www.w3.org/1999/xhtml" a-long-attribute-name2="{{}}'&quot;&#9; &gt;bca{{ac&gt;cé &#9;&#9;&amp;&quot;c&lt;c&quot;" n3=""><script xmlns:p="urn:p"></script></title>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "doctype-system": "case.dtd" })
