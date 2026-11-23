declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<data-element id0="{{é&quot;é zy}}&lt;&#9;" n1="&gt; y'x&gt;" n2="&amp;ay&gt;écz&quot;a&lt;{{ " a-long-attribute-name2="z" a-long-attribute-name0="cé&amp;xxcy{{ " type0="&lt;&lt;a&lt;&#10;b}}b&#9;&#10;xx&gt;"></data-element>, <item xmlns:p="urn:p" id0="xb&quot;abc x" id3="ya&#9;}}&gt;}}é &lt;éé" a-long-attribute-name2="x&#10;&#10; x&amp;}}a&amp;y&lt;&lt;é{{y'bxy&quot;&amp;}}{{&lt;'{{&#9;&amp; "> <!---->
&lt;&gt;&lt;   </item>, <q:section xmlns:q="urn:example:q" type2=" &#10;{{cé&gt;x&amp;z&amp;" id1=" {{azy}}c}}&gt;&lt;é&#10;&quot;z&lt;}}&#10;&gt;&lt;{{'zéy&amp;&amp;z&gt;&#10;" v2="}}{{&#9;y&#9;&lt;"></q:section>), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "cdata-section-elements": (QName("urn:example:q", "q:section")), "suppress-indentation": (QName("", "data-element")), "item-separator": "|" })
