declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<script a-long-attribute-name1="ac&lt;'zz}}z" v0="&#9;&lt;&amp;&gt;a&lt;'&amp;}}&amp; éxébaaz&lt;x"></script>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "html-version": 4.0 })
