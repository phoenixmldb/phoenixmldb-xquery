declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<html xmlns="http://www.w3.org/1999/xhtml"><p xmlns:long-prefix="urn:example:a-much-longer-namespace-uri" a-long-attribute-name3="xzczéé&lt;&#9;bb}}}}y&gt;'" n3="yb&#9;&lt;a{{}}&quot;z&quot;&quot;" n1="ax&lt;yz"></p></html>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "suppress-indentation": (QName("", "p")), "html-version": 5 })
