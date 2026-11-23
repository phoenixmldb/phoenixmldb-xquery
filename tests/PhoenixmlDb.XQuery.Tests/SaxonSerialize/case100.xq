declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <title xmlns="http://www.w3.org/1999/xhtml" type2="&#9;a&#10;{{x&#9;&#9;&#10;y&gt;&quot;bé c&#9;}}&quot;é&gt;x&lt;c" n2="}} zz'&gt;xb b&#9;" id3="xz'&#10;&#10;&gt;&amp;z&lt;{{éx&#10;&quot;&amp;{{{{a&#9;" id1="  z{{&lt;y&gt;a&amp; &#10;{{&amp;z&#9;y {{y x" a-long-attribute-name3="a&lt;z&amp;&gt;z&#10;éa&#10;&quot;é &#10;&#9;c ybzy"></title> }), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "doctype-system": "case.dtd", "cdata-section-elements": (QName("", "title")) })
