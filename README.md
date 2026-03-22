# PhoenixmlDb XQuery

A modern XPath/XQuery 4.0 engine for .NET with Full-Text Search support.

## Features

### XPath/XQuery 4.0
- **76 new 4.0 functions** across fn:, math:, map:, and array: namespaces
- **Record types**: `record(name as xs:string, age as xs:integer)`
- **Enum types**: `enum("red", "green", "blue")`
- **Union types**: `union(xs:string, xs:integer)`
- **Thin arrow operator**: `$x -> upper-case()`
- **FLWOR `for member`**: iterate array members
- **FLWOR `otherwise`**: fallback for empty results
- **Keyword arguments**: `f(name := value)`
- **`not` keyword, `otherwise` operator, `while` clause**

### XQuery Update Facility 3.0 (Parsing Only)
- `insert`, `delete`, `replace`, `rename` — parsed and compiled but **not yet executable** (throws `XUST0002`)
- `transform copy ... modify ... return` — parsed but **not yet executable** (throws `XUST0002`)
- Pending Update List (PUL) infrastructure and `PendingUpdateApplicator` are implemented; awaiting `IUpdatableNodeStore` integration

### XQuery Full-Text 3.0
- `contains text` expressions with `ftand`, `ftor`, `ftnot`
- Stemming, language-aware analysis via Lucene.NET
- Position-based matching: `ordered`, `window N words`
- BM25 relevance scoring via `ft:score()`
- Built-in thesaurus support

## Installation

```bash
dotnet add package PhoenixmlDb.XQuery
```

## Quick Start

```csharp
var facade = new XQueryParserFacade();
var expr = facade.Parse("1 + 1");
// Evaluate with QueryExecutionContext
```

## License

Apache 2.0 — see [LICENSE](LICENSE)

## Related Projects

- [phoenixmldb-core](https://github.com/phoenixmldb/phoenixmldb-core) — Core types and XDM
- [phoenixmldb-xslt](https://github.com/phoenixmldb/phoenixmldb-xslt) — XSLT 4.0 engine
- [phoenixmldb-cli](https://github.com/phoenixmldb/phoenixmldb-cli) — CLI tools
