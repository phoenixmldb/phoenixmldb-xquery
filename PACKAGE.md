# PhoenixmlDb.XQuery

XQuery 4.0 query engine for [PhoenixmlDb](https://phoenixml.dev) — query XML and JSON documents with the full power of XQuery.

## Features

- **Full XQuery 4.0** — FLWOR expressions, constructors, modules, user-defined functions
- **XPath 4.0** — complete XPath implementation with 240+ built-in functions
- **JSON support** — `json-doc()`, `parse-json()`, maps, arrays — query JSON natively
- **Full-text search** — `ft:contains()` with stemming, wildcards, proximity, scoring
- **Update Facility** — insert, delete, replace, rename, transform expressions
- **Type system** — records, enums, union types (XQuery 4.0)

## Quick example

```csharp
using PhoenixmlDb.XQuery.Execution;

var engine = new QueryEngine();

// Simple query
var results = engine.ExecuteToListAsync(
    "for $x in 1 to 10 where $x mod 2 = 0 return $x * $x");

// Query XML documents
var books = engine.ExecuteAsync(
    "//book[price > 30]/title",
    containerId);
```

## Related packages

| Package | Description |
|---------|-------------|
| **PhoenixmlDb.Core** | Core types and XDM data model (dependency) |
| **PhoenixmlDb.Xslt** | XSLT 4.0 transformation engine |
| **PhoenixmlDb.XQuery.Cli** | `xquery` command-line tool |

## Documentation

Full documentation at [phoenixml.dev](https://phoenixml.dev)

## License

Apache 2.0
