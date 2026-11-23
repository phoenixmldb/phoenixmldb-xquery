declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((document { <img xmlns:q="urn:example:q" xmlns="http://www.w3.org/1999/xhtml" id3="&#9;&amp; '&amp;&amp;xb}}&quot;&quot;yéc&#9;{{&quot;éy&gt;  cycé cyc"></img> }, <style xmlns="http://www.w3.org/1999/xhtml"><!-->zé"--></style>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "item-separator": "|" })
