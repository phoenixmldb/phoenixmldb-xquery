declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<q:a xmlns:q="urn:example:q"><data-element q:id1="' &lt;z ' 'a&amp;&amp;&#10;{{&lt;&#10;&gt;&quot;}}&lt; {{" a-long-attribute-name3="z&amp; é}}&amp;é&amp;&amp; a" v1="é&amp;" q:v3="bbczc&amp;&#10;}}a'éz&quot;'&lt;}}&#9;z&#9;c&lt;" id2="y"><q:section><q:item n3="'é&#9;&gt;&lt;" a-long-attribute-name1="b&gt;&#9;b&amp;x}}{{"><!--y">--><item q:n2="b}}&lt;b&#9;&amp;&gt;&#9;"></item></q:item>   </q:section><data-element xmlns:p="urn:p" p:type2="&#9;c &#10;&gt; }}&gt;y &lt;cx' '" xml:space="preserve"></data-element></data-element></q:a>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "cdata-section-elements": (QName("urn:example:q", "q:section")) })
