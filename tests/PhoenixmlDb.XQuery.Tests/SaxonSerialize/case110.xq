declare boundary-space preserve;
declare namespace p = "urn:p";
declare namespace q = "urn:example:q";
declare namespace long-prefix = "urn:example:a-much-longer-namespace-uri";
serialize((<td xmlns="http://www.w3.org/1999/xhtml" id1=""><section xmlns:q="urn:example:q" xml:space="preserve">écc"é"</section></td>), map { "method": "xhtml", "indent": true(), QName("https://schemas.phoenixml.dev/2026/functions", "indent-style"): "saxon-he-10", "cdata-section-elements": (QName("", "td")), "suppress-indentation": (QName("", "td")), "item-separator": "|" })
