declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <section><?pi2 yax?><section v1="éax&amp;a&amp;&#10; }}'&quot;c&gt;&quot;y'c&quot;y&#10;'xxx'z" type2="é&#9;b&amp;b&lt;z&#9;}}zéc&lt;'&lt;" xml:space="preserve">&amp;x&amp;{{&lt;c{{{{yz<!-->z{--><!--	{<cxé{"-->   </section><section xml:space="preserve"></section></section> }), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "cdata-section-elements": (QName("", "section")) })
