declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<title xmlns="http://www.w3.org/1999/xhtml">&lt;<long-prefix:span xmlns:long-prefix="urn:example:a-much-longer-namespace-uri" a-long-attribute-name0="zaa&quot;&lt;&#9;&lt;a&#10;é}}yx&amp;&quot;é&lt;x}}&amp;é&amp;z}}xz&#9;&amp;y&#9;" long-prefix:type1="&#10;a&amp; &lt;y}}y&quot;&quot;{{x&#9;&#9;&amp;{{" v1="&lt;xé&#10;'&#9;b{{" a-long-attribute-name1="&amp;b&quot;&#10;&lt;"></long-prefix:span></title>, <textarea></textarea>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "html-version": 4.0 })
