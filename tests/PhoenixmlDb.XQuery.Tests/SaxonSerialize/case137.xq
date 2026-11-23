declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<p:section xmlns:p="urn:p" a-long-attribute-name2="é {{&quot;zé{{y&#9;z'éac{{{{&quot;&quot;&#10;yz'a" p:type2=" b&quot;é&quot;&#10;&lt;c'é"></p:section>, "&amp;""<}", <x id1="&amp;x'&lt;" v2="xé}}c x&quot; &#9;&gt;x }}z&#9;z&lt;&quot;z{{&quot;ca&quot;z"></x>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "item-separator": "|" })
