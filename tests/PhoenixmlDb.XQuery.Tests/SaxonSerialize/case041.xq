declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <div xmlns="http://www.w3.org/1999/xhtml" id2="zb &#9;&gt;c&#9;'" n1="aca&quot;c }}" id0="{{{{&#9;" v2="&#9;&gt;'&#10;'}}z&gt;&amp;&lt;a&quot;&lt; &amp;c&#9;y&lt;y}}" a-long-attribute-name1="éc&amp;{{&amp;&amp;'b&lt;c&#9;{{xb&#9;y"></div> }), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false() })
