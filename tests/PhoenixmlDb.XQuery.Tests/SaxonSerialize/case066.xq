declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <p:span xmlns:p="urn:p" id2="a&#10;'z}}&amp;b&#10;éz'y&#9;&gt;{{é caa&quot;aa"><!--"'>&z}>x -->   <!--}--></p:span> }), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
