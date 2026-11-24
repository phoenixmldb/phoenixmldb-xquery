declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<li xmlns="http://www.w3.org/1999/xhtml" id1="axé&amp;}}&lt;}}yéb&lt;xxba&amp;zbébb" a-long-attribute-name0="&lt;&gt;z'z&gt;&lt;b{{&gt;x{{"></li>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "html-version": 5.0 })
