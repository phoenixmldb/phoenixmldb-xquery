declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <q:li xmlns:q="urn:example:q" xmlns="http://www.w3.org/1999/xhtml" n3="&quot;x&#10;&#9;" id0="&#9;&#10;&#9;&lt;éx}}x }}}}b&#9; z}}c {{{{accbé&amp;" a-long-attribute-name2=" é{{'a&lt;y}}&#9; }}{{&gt;aa" q:a-long-attribute-name0="&quot;&gt; 'cay &#10;ébz&#10;&amp;bcxéc&lt;&amp;&#10;é&#10;&quot;y b a" v1="{{&gt;" type0="y&gt;x&quot;&amp;&quot;yba&amp;&lt;z'&gt;&#10;&#10;cb&gt;&#10;&lt; z &gt;&#9;"></q:li> }), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "doctype-system": "case.dtd" })
