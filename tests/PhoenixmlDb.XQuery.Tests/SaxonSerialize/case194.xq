declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<p xmlns:q="urn:example:q"><!--xé'{"}''	--><title v2="xc&quot;bc{{&lt;y&gt;&lt;y}}{{&gt;{{&#9;x&lt;cbé&gt;&quot;" id2="&gt;" q:v1="&gt;&quot;'yé &lt;ac&#9;&#10; xyycz&#9;xéa&quot;z&lt;''" q:a-long-attribute-name3="'zé&amp;c&quot;&#10; ayx&#9; &gt;'z&gt;&amp;{{c">   aa}}}}y<q:section v3=" &quot; &#9;&#10;"></q:section></title>   </p>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "html-version": 5 })
