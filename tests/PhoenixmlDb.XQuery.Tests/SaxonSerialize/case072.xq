declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<x xmlns:q="urn:example:q" type1="x&lt;&lt;{{ z&#10;{{}}acz&quot;x&quot;&amp;'yz" v2="'y&#9;b&#10;" q:a-long-attribute-name3="'&#9; xéx&lt;&#9;x&lt;" v0="&amp;&quot;'x&lt;&lt;&#10;éé&lt;}}&quot;'{{&lt;y&gt;" xml:space="preserve"></x>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "doctype-system": "case.dtd", "doctype-public": "-//PHX//CASE//EN" })
