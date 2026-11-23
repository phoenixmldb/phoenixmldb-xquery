declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <item xmlns:p="urn:p" id2="{{&lt;c&lt;x&amp;&quot;y&lt;a{{cc x&#10;}}xyxé" n0="y&gt;yé&amp;{{}}aé&#9;{{}}&amp;c&gt;}}&#10;&quot;&amp;z" xml:space="preserve">	"&gt;'y"&amp; éz
<a n2="bc&gt;{{}}&quot;" v1="&#10;&gt;xx&lt;&#10;&gt;yax" v3="y&#9;z&amp;&#9;{{&lt;&#10;}}&amp;&amp;a'&quot;&gt;c'yy&amp;" id0=" }}z&#10;&quot;zyzbz cé&gt;}}&#10;éx''&#10;ya z&gt;&#9;&#9;" type0="&lt; z&quot;b&amp;&amp;&#10;{{&amp;a&gt; &#10;{{yzb é&quot;xb{{"></a></item> }), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10" })
