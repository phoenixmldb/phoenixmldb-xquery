declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<p:x xmlns:p="urn:p" v2="cbbxc&#10;é" n2="bxcxy&gt;}}xab'&amp;a&quot;a}}{{z{{">}}" cb&gt;}}	 bé'<!--<<
{&"	
<--><p:item a-long-attribute-name3="é&#9;&lt;{{{{&quot;" type2="&gt; &#9;&gt;&gt;a{{}}c&#10;'&quot;}}z'&lt;&lt;b&amp;x'bcy"></p:item></p:x>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false() })
