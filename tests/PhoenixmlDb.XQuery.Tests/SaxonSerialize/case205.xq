declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<b xmlns="http://www.w3.org/1999/xhtml">   <a v0="&quot;xa'y'éaéz" n3=" x yy&#10;&#9;&#9; {{zé&lt;zéb é&lt;y&amp;" a-long-attribute-name2="éé&gt;&#10;&lt;&quot;&gt;' '}}é{{é&amp;" v3="zé'c&amp;" id3="c&#10;{{&quot;'éc&quot;" n2="&quot;&#9;&#9;é'&#9;&quot;&#10;&#9;y&amp; &quot;&amp;b&amp;&#9;">ab&amp;a<em n3="zc&#10;&quot;ézc&#10;&#9;" a-long-attribute-name1="&quot;a&quot;z&gt;"></em><?pi1 
 aé ?></a><a></a></b>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "html-version": 5 })
