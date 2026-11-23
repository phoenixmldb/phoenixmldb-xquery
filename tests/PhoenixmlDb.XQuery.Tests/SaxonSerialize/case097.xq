declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <B xmlns="http://www.w3.org/1999/xhtml" a-long-attribute-name3="&quot;" type2="}}cya&gt;&lt;&amp;&gt;a&#10;&#10;&gt;&#9;zz'&lt;"><q:pre xmlns:q="urn:example:q"><!--a'yzx{>&
é--></q:pre><section xmlns:long-prefix="urn:example:a-much-longer-namespace-uri" v1="a{{cb&#9;&#9;&gt;"><!-->ac
é'--></section>&amp;c  &amp;<?pi0 é}>}
?></B> }), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false() })
