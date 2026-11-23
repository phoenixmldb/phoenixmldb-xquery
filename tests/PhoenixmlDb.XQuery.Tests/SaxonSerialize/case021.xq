declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <b v2="&#9;&lt;&lt;'{{&gt;ax&#9;&lt;" a-long-attribute-name2="}}y&#10;&#9; &lt;{{ &amp;&gt; x&amp;&#10;&#9;b{{&#10;yzéa'&quot;{{b x"><e xmlns:q="urn:example:q" id1="{{}}&#10;x" id3="&gt;x'x&#9;&quot;'{{&lt;x"><!--' zy{'		>
--><!--c	b
{--><!--zxcé>c{"}--><q:section><data-element type2="&#9;&#9;z&lt;é&quot;&gt;&amp;z&quot;z'&amp;a&gt;&lt;'&#9; '&gt;" n2="cb&#10;x&gt;&gt;&#9;&quot;&lt;&#9;é&quot;&#10;xéby&#10;'éc&#9;é"><a q:id1="'&lt;&quot;az é}}&gt;" type2="é&amp;x&amp;}}&quot;&gt;'{{&amp;&lt;"></a><!--éééé&"a{y--></data-element>}}y<q:section><b type2="y&quot;'a   " id1="zy&quot;x{{bc'&#10;y&#10;&quot;x zx&amp;&gt;b'&#10;&#10;x b&amp;&#10;" q:id0="cc&amp;}}&quot;a&lt;{{a&#10;a&gt; b&gt; '{{" xml:space="preserve"></b>ay&gt;{{z
"{{a</q:section></q:section></e><b xmlns:p="urn:p" p:v1="&quot;&lt;&gt;&#10;{{&#9;xz{{a'&lt;&lt;'&#9;yy'&lt;aa&#9;&#9;x}}yéé"><p:b>   </p:b><!-->&>b'}'--></b></b> }), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "suppress-indentation": (QName("", "b"), QName("urn:example:q", "q:section")) })
