declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<html xmlns="http://www.w3.org/1999/xhtml"><img a-long-attribute-name1="xy" a-long-attribute-name2="&gt;&gt;''&amp;x}}&#9;c }}xxc&#10;" a-long-attribute-name0="a&amp; {{'z&lt;béc &#9;b '&gt;bb'&#10;&amp;za}}éc}} &gt;" type1="&gt;a" type0="{{y caa'&#9;xy&lt;xx&gt;z&gt;&gt;xybaa"></img></html>, <TD xmlns="http://www.w3.org/1999/xhtml" n2="c {{&#10;&amp;é{{bz{{&gt;}}é&#10;béx&gt;&gt;{{&lt;ycc{{a&#9;y&gt;&gt;"></TD>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "cdata-section-elements": (QName("", "TD")), "html-version": 5.0 })
