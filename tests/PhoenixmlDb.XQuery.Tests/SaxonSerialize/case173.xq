declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<html xmlns="http://www.w3.org/1999/xhtml"><section xmlns="http://www.w3.org/1999/xhtml" id1="cxc&amp;}}yé{{é&#9;&quot;é&lt;c&quot;&quot;'}} '&amp;x" id2="&quot;&#10;é" n1="b&quot;&#10;}}}}cyy{{a}}zéé&#10;z&#10;b&gt;}}b}}" type1="zx{{y&#9;ayz&lt;ac&#9; &lt;&quot;&amp;c'}}&#9;é}}&lt;b{{&quot;ccy"></section></html>, <img type2="xa &#10;"></img>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "omit-xml-declaration": false(), "item-separator": "|", "html-version": 4.0 })
