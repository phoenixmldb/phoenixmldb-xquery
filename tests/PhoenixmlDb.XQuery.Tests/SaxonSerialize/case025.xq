declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <td xmlns:long-prefix="urn:example:a-much-longer-namespace-uri"><!--z--><b id0="é{{z&lt;ya&amp;c'&quot;&amp;aéa&quot;&gt;&#10;&lt;a&#9;&#10;&gt; &quot;x&amp;&#9;" v0="" xml:space="preserve">      </b><?pi1 yy&z
x?></td> }), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "doctype-system": "case.dtd", "doctype-public": "-//PHX//CASE//EN" })
