declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <li xmlns="http://www.w3.org/1999/xhtml" a-long-attribute-name3="cbbé}}b}}é&lt;&#9;&amp;&#10;c b&#10;é&amp;}}{{ééy" v2="z" n0="{{ac&lt;é&quot;&#9; &lt;éxz}}c{{&gt;b{{bzbé&gt;a&gt;z"><title n3="éxx&quot;&#10;" id2=""></title><!--
b"
 
'--></li> }), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
