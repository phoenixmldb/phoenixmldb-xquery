declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<TEXTAREA a-long-attribute-name3="&#9;bc&#9;&lt;' &#9;aa{{&quot;&quot;'c}}x&quot;yy'xx'{{" a-long-attribute-name0="}}&quot;&#10;c'b}}'{{'b'z&quot;'a&gt;éy'b}}{{&amp;&lt;x&#9;é}}" id1="y'bcby&amp;za&gt;  &quot;&#9;z&#9;}}&quot;bc&amp;y&gt;&quot;x}}ac" id0="&#10;a&gt;bb' &gt;}}&quot;é&lt;'}}bcb{{xbc yz&#10;&#9;c">   &lt;b   <pre xmlns:q="urn:example:q" v0="é&#10;" q:a-long-attribute-name1="&#10;axxé&lt;{{xé'&amp;'&#10;'aé}}&lt;é&gt;&gt;xc&#10;"></pre></TEXTAREA>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "suppress-indentation": (QName("", "TEXTAREA")) })
