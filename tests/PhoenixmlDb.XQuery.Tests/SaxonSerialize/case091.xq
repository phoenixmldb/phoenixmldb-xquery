declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <x xmlns:long-prefix="urn:example:a-much-longer-namespace-uri" v2="&gt; &gt;a &amp;y&amp;&#10;&#10;az&gt;y{{b&quot;{{" id0="y&lt; éb' &quot;&#9;}}'c&amp;ac&gt;b"><a></a><x xmlns:q="urn:example:q"></x><e><long-prefix:section long-prefix:v0="zx&#10;{{&lt;zéé&#10;}}&lt;é&#10;{{&#9;'  a&amp;&amp;&amp;b&#10;" id2="ééaz&gt;}}&#9;'a&lt;}}'}}é}}"></long-prefix:section></e></x> }), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
