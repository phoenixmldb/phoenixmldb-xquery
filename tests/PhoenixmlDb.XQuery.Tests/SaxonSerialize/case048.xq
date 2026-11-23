declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <a xmlns="http://www.w3.org/1999/xhtml" v0="b}}z&quot;b&#10;&gt;x{{{{}}}}&lt;&amp;}}&#10;&lt;éb'y&amp;" n0="y&#10;y{{&lt;é}}&gt;z&quot;}}é&lt;xé}}&#9;&quot;&#9;&amp;&#9;{{">   </a> }, <div xmlns="http://www.w3.org/1999/xhtml"></div>, <item xmlns="http://www.w3.org/1999/xhtml" n1="" a-long-attribute-name2="}}&amp;yab"><textarea type1="&gt;by&quot;xaé" id3="" xml:space="preserve"></textarea>é 		"z'aac</item>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "cdata-section-elements": (QName("", "div")) })
