declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<script a-long-attribute-name1="&#9;&quot;é&quot;&lt;}}}}&gt;byy&amp;é&#10;zxy {{bba&gt;{{&amp;c&amp;"></script>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "doctype-system": "case.dtd", "doctype-public": "-//PHX//CASE//EN", "html-version": 5 })
