declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<html xmlns="http://www.w3.org/1999/xhtml"><div xmlns="http://www.w3.org/1999/xhtml" a-long-attribute-name2="}} &quot; c" a-long-attribute-name3="b&#9;xéz&gt;'' '&gt;y}}a  &lt;&quot;"></div></html>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "doctype-system": "case.dtd", "doctype-public": "-//PHX//CASE//EN", "html-version": 1.0 })
