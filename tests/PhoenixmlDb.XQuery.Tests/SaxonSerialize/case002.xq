declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<ITEM xmlns="http://www.w3.org/1999/xhtml"><li><li a-long-attribute-name1="&#9;" a-long-attribute-name2="&quot;{{é&lt;&#9;xyéy&#9;}}byzc&#9;''y&quot;&amp;&#10;&lt;'y&quot;'&#9;x" v0="&gt;x&quot;&lt;b"></li></li><img xmlns:p="urn:p" a-long-attribute-name0="''&#10;z&lt;a{{&amp; &gt;{{b&gt;&gt;c" id1="{{ b&quot;" p:id3="{{a'é&#9;&#9;b" type3="&quot;&quot;&amp;&gt;&#9;&lt;&quot; }}}}&gt;}}a &#10; x&#9;y&lt;&quot;ax" type0="&#9;&gt;&#10;&#9;}}a&amp;ax&quot;'cbxa''ax" v0="&#10;ébé"><textarea a-long-attribute-name3="&lt;éabcxz&quot;}}x'}}éa&#10;b &amp;{{ }}&#9;"></textarea></img>z b'yz'xb" </ITEM>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "suppress-indentation": (QName("", "li")) })
