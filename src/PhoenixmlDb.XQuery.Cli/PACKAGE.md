# xquery

Command-line XQuery 3.1/4.0 processor for .NET. Query XML documents from the terminal using the PhoenixmlDb XQuery engine.

## Installation

```bash
dotnet tool install -g xquery4
```

## Usage

```bash
# Query an XML file
xquery '//book/title' library.xml

# Count elements
xquery 'count(//item)' catalog.xml

# Read from a query file
xquery -f transform.xq input.xml

# Query a directory of XML files
xquery 'collection()//product[price > 50]' ./data/

# JSON output
xquery -o json 'map { "count": count(//item) }' data.xml

# Read from stdin
cat data.xml | xquery '//item/@name'

# Show execution plan
xquery --plan 'for $x in 1 to 10 return $x * $x'

# Show timing breakdown
xquery --timing '//item' large-catalog.xml
```

## Features

- **XQuery 3.1/4.0** — FLWOR, maps/arrays, higher-order functions, string constructors
- **Multiple output methods** — adaptive, XML, text, JSON
- **Context item** — input XML is available as `.` (standard XQuery)
- **Multiple sources** — files, directories, URLs, stdin
- **Full prolog support** — namespaces, variable/function declarations, serialization options
- **Execution plans** — inspect how queries are compiled and optimized
- **Timing** — built-in performance profiling

## Documentation

Full documentation at [phoenixml.dev](https://phoenixml.dev/tools/xquery-cli.html)

## License

Apache-2.0
