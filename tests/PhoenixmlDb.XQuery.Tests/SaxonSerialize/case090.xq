declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <a type3="&amp;&#9;&lt;'yc&lt;é&quot;}}&gt;" a-long-attribute-name1="&gt;}}&quot;&#10;&#10;'x" n2="b{{{{b&#9; bx&#10;&#9;'}}éaé é&#10;&gt;&#9;{{''&gt;" type1="&lt;&amp;yéa&gt;&#10;yy &quot;}}&lt;c&gt;é'x" type2="&amp;é&#9;xcbx" n1="y&lt;c}}yzxby&amp;'&lt;}}&amp;"></a> }), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
