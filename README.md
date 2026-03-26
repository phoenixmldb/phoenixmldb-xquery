# PhoenixmlDb XQuery

A modern XPath/XQuery 4.0 engine for .NET with Full-Text Search and Update Facility support.

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

### XQuery 3.1
- **Annotations**: `%public`, `%private`, `%updating` on function and variable declarations
- **Module imports**: `import module namespace ns = "uri" at "location"`
- **External variables**: `declare variable $x external;` with runtime binding via `SetExternalVariable()`
- **Direct element constructors**: `<element attr="val">text {expr} more</element>`
- **UCA collations**: Unicode Collation Algorithm with `lang`, `strength`, `fallback` parameters

### JSON Support
- `fn:parse-json()` — parse JSON strings into XDM maps and arrays
- `fn:json-doc()` — load and parse JSON files
- Maps (`map:*`) and arrays (`array:*`) — full XQuery 3.1 + 4.0 support

### XQuery Update Facility 3.0
- `insert`, `delete`, `replace`, `rename` — fully operational via `InMemoryUpdatableNodeStore`
- `transform copy ... modify ... return` — fully operational with deep copy and PUL application
- Pending Update List (PUL) with spec-compliant 6-phase ordering

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

### Command-Line Tool

A standalone `xquery4` CLI tool is also available as a .NET global tool:

```bash
dotnet tool install -g xquery4
```

```bash
# Evaluate an expression
xquery4 '1 + 1'

# Query an XML file
xquery4 '//title' books.xml

# Run a query file with timing
xquery4 --timing -f transform.xq input.xml

# Pipe XML from stdin
curl http://example.com/data.xml | xquery4 '//item/@name'
```

Run `xquery4 --help` for the full list of options.

## Quick Start

```csharp
using PhoenixmlDb.XQuery;

var xquery = new XQueryFacade();

// Simple expression
string result = await xquery.EvaluateAsync("1 + 1");
// result: "2"

// Query with XML input
string titles = await xquery.EvaluateAsync(
    "//book/title/text()",
    "<library><book><title>XSLT 3.0</title></book></library>");

// FLWOR expression
string csv = await xquery.EvaluateAsync("""
    for $i in 1 to 5
    return $i * $i
    """);

// Query with context document (available as . and $input)
string authors = await xquery.EvaluateAsync(
    "for $b in //book return $b/author/text()",
    File.ReadAllText("catalog.xml"));
```

## API Overview

### Query Execution
- `EvaluateAsync(string xquery)` — evaluate and return result as string
- `EvaluateAsync(string xquery, string inputXml)` — evaluate with XML context item
- `EvaluateAllAsync(string xquery)` — return each result item separately

### External Variables (via QueryExecutionContext)
- `context.SetExternalVariable(string name, object? value)` — bind before evaluation
- `context.SetExternalVariable(QName name, object? value)` — bind with namespace

### Parsing
- `XQueryParserFacade.Parse(string xquery)` — parse to AST
- `XQueryParserFacade.TryParse(string xquery, out errors)` — parse without throwing

## License

Apache 2.0 — see [LICENSE](LICENSE)

## Related Projects

- [phoenixmldb-core](https://github.com/phoenixmldb/phoenixmldb-core) — Core types and XDM
- [phoenixmldb-xslt](https://github.com/phoenixmldb/phoenixmldb-xslt) — XSLT 4.0 engine
- [phoenixmldb-cli](https://github.com/phoenixmldb/phoenixmldb-cli) — CLI tools
