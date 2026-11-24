declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<html xmlns="http://www.w3.org/1999/xhtml"><textarea xmlns="http://www.w3.org/1999/xhtml" v3="&amp;x &#9;&lt;ay&amp;ay{{&amp;'{{ ééxc&lt;&#9;c" type3="'&lt;'bbx&lt;{{&lt;b&amp;}}a&amp;&quot;é&#10; y&lt;" n0="a&#9;&gt;'&gt;&lt;az">   </textarea></html>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "cdata-section-elements": (QName("", "textarea")), "html-version": 5.0 })
