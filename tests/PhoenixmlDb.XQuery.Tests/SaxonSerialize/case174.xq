declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<html xmlns="http://www.w3.org/1999/xhtml"><p xmlns="http://www.w3.org/1999/xhtml" v0="&#9;&gt;" xml:space="preserve"></p></html>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "suppress-indentation": (QName("", "p")), "html-version": 5.0 })
