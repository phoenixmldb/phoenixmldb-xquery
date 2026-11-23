declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <textarea xmlns:q="urn:example:q">x&lt;ya}}éyx<?pi1 <?><!--y{cc--><?pi2 {{?></textarea> }), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "cdata-section-elements": (QName("", "textarea")), "suppress-indentation": (QName("", "textarea")) })
