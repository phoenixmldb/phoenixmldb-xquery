declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<script xmlns="http://www.w3.org/1999/xhtml" a-long-attribute-name2="a&#10;y&#9;z&quot;{{&#10;&lt;&quot;&lt;&lt; bzz&#10;é}}x" n1="az '" n3="a&#10;&#9;a{{&amp;b{{bxz'&gt;cx" v1="'yz'&gt;xéc}}&gt;a&#10;&lt;&quot;}}&quot;''&gt;xcx&amp;&amp;c}}'"></script>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "doctype-system": "case.dtd" })
