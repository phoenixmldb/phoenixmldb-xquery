declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<x a-long-attribute-name2="z&lt;b&lt;y&amp;}}'" v2="z{{" xml:space="preserve"><item><?pi2 x}}>'?><!--	yé

b"yzx--></item></x>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "suppress-indentation": (QName("", "item")) })
