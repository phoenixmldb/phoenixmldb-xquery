declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <item xmlns:q="urn:example:q"><section q:n1="&lt;x&#9; z" v3="c" type3="&#9;a&lt;xzca {{&lt;&amp;c{{xx x&quot;&#9;}}&lt;"></section><!--&xzy{{&	x'--></item> }), map { "method": "xml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "html-version": 5 })
