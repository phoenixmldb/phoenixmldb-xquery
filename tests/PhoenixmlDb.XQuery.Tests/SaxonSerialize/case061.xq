declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <item xmlns="http://www.w3.org/1999/xhtml" a-long-attribute-name1=" é c">   <b v3="'éa&lt;&lt;éx&#10; &gt;a&gt;&amp;c" type0="x&amp;c&#10;&lt;{{ " a-long-attribute-name3="c&#9;" n3="c &lt;b"><!-->
}&c-->
&gt;&amp;&gt;" 
"'</b><style xml:space="preserve"><?pi0 ?></style><!--yyzé}za}bz--></item> }), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "doctype-system": "case.dtd", "suppress-indentation": (QName("", "style")) })
