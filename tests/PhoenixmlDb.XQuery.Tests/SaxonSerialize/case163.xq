declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<html xmlns="http://www.w3.org/1999/xhtml"><textarea xmlns:long-prefix="urn:example:a-much-longer-namespace-uri" xmlns="http://www.w3.org/1999/xhtml" id3="&amp;z&quot;x&gt;' é&lt;" xml:space="preserve">a"y}}x</textarea></html>, <item a-long-attribute-name0=""></item>, <section v0="'azy&amp;" v3="}}axy&lt;{{b&quot;}}&quot;&#9;&quot;cbc" n3="" type1="}}y{{}}'" a-long-attribute-name2="c&lt;&lt;béz'a'xé&#10;z{{yc&quot;écz&amp;&gt;&gt;"></section>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "item-separator": "|", "html-version": 4.0 })
