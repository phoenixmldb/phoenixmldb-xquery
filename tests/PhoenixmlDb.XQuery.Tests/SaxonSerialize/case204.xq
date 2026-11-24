declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<html xmlns="http://www.w3.org/1999/xhtml"><pre xmlns="http://www.w3.org/1999/xhtml" v1="{{{{aéca&quot;zccbé&#9;&amp;"><a type0="&gt;&quot;{{aabb&amp;&quot;zx&quot;aycza{{{{&#9;" v3="&#9;b&amp;yz}}z}}{{a&#10;{{a}}cb&amp;"><script></script></a></pre></html>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "html-version": 5 })
