declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<e a-long-attribute-name1="{{&#10;y&amp;x é&gt;"></e>, <data-element n2="'}}a}}&gt;&quot;&#9;&quot;a" n0="x}}'{{&#10;yy&#9;{{éz&#9;  b&gt;yéb{{&#10;" type1="é{{&lt;'&lt;{{acx&amp; &#9;b&#9;&amp;}}}}&lt;&lt;'z&amp; &quot;&gt;&quot;&lt;" n3="&#10;'&quot;{{y&lt;}}&#9;&#9;&gt;c"></data-element>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "item-separator": "|" })
