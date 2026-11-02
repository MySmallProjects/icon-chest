# **IconChest Phase 5 Complete: The Query Language**

The hand-written four-stage search pipeline, API endpoints, CLI tooling, and documentation for **Phase 5: The Query Language** have been built, integrated, and verified with all 412 tests passing.

---

### **Key Deliverables Implemented**

1. **Four-Stage Query Pipeline (`src/IconChest.Core/Query/`)**
    
    - **Lexer (`QueryLexer.cs`):** Fast, non-allocating UTF-16 code unit position tracking, handles identifier quoting, escape sequences (`\"`, `\\`, `\*`, `\?`), string literals, operators (`==`, `!=`, `<`, `<=`, `>`, `>=`, `~=`, `!~=`), connectives (`and`, `or`, `not`), keywords (`in`, `like`, `under`), and reports exact character locations.
    - **Parser (`QueryParser.cs`):** Hand-written recursive descent precedence parser (`or` < `and` < `not` < primary), enforcing syntax limits (max depth 20, max terms 200, max in-values 100, max pattern length 200) and reporting first syntax error fast.
    - **Validator (`QueryValidator.cs`):** Semantic binder over all 20 fields (`FieldRegistry.cs`). Converts units (`kb`, `mb`, `gb`), date boundaries (UTC, leap years, timezone offsets, daylight savings), validates rating ranges (0-5), resolves boolean shorthand (`transparent` →\to→ `transparent == true`), checks wildcard operators, and collects up to 10 semantic errors with typo suggestions (Levenshtein distance ≤2\le 2≤2).
    - **Compiler (`QueryCompiler.cs`):** Compiles validated AST into parameterised SQL `WHERE` clauses (`@q0`, `@q1`, ...). Implements strict two-valued boolean logic (`width != 100` includes rows with `NULL` dimensions), tags existence subqueries, folder `under` path queries, and implicit `is_hidden = 0 AND is_missing = 0` filtering unless explicitly queried.
    - **Formatter (`QueryFormatter.cs`):** Canonical printer with minimal parentheses: Parse(Format(Parse(x)))==Parse(x)\text{Parse}(\text{Format}(\text{Parse}(x))) == \text{Parse}(x)Parse(Format(Parse(x)))==Parse(x).
    - **Filter Converter (`SimpleFilterToQuery.cs`):** Converts structured `ImageSearchRequest` into an equivalent query string, reporting any unsupported filters.
    - **Engine Facade (`QueryEngine.cs`):** Single entry point for `Validate` and `ParseAndCompile`.
2. **Deterministic Custom SQLite Functions**
    
    - Registered `iq_eq(a, b)` (case- and accent-insensitive Unicode comparison) and `iq_like(value, pattern)` (delegating to `WildcardMatcher` with case/accent folding and wildcard escape awareness) via `DatabaseConnectionFactory`.
3. **Database Migration `0005_nullable_dimensions.sql`**
    
    - Recreation migration permitting `width` and `height` to be `NULL` for unreadable or unsupported image files while maintaining all foreign keys and search indexes.
4. **HTTP Endpoints (`src/IconChest.Api/Endpoints/QueryEndpoints.cs`)**
    
    - `GET /api/images?q=...` & `GET /api/images/ids?q=...`: Connected to the query engine. Rejects mixing `q` with structured filters (400 ProblemDetails); returns RFC 7807 problem details with `queryErrors` when queries are invalid.
    - `POST /api/query/validate`: Validates query text without executing, returning syntax and semantic errors.
    - `GET /api/query/help`: Provides metadata on all fields, supported operators, types, aliases, and 30+ examples.
    - `POST /api/query/from-filter`: Converts a structured filter payload into query text.
5. **Dev CLI Commands (`src/IconChest.Cli/QueryCliCommands.cs`)**
    
    - `query validate "<query>" [--sql]`: Validates queries with terminal caret error indicators.
    - `query run "<query>" [--sort <field>] [--dir asc|desc] [--limit <n>]`: Executes queries directly and formats matches.
    - `query explain "<query>"`: Dumps the untyped AST, compiled SQL, parameters, and SQLite `EXPLAIN QUERY PLAN`.
    - `query docs [--write]`: Outputs or writes docs/QUERY-LANGUAGE.md.
6. **Generated Reference Documentation**
    
    - docs/QUERY-LANGUAGE.md generated directly from the codebase definitions and locked by golden unit tests.

---

### **Test Suite Verification**

All **412 tests** across the entire solution are passing cleanly (0 failures, 0 skipped):

- **`IconChest.Core.Tests`**: 369 passed (including 203 new query language unit, semantic, fuzz, date, benchmark, and golden tests).
- **`IconChest.Api.Tests`**: 43 passed (including query endpoint integration tests).



---


# How can I test Phase 5?

You can test Phase 5 at three levels: **automated tests**, the **developer CLI**, and the **HTTP API**.

---

### **1. Run the Automated Test Suite**

Run the full suite (412 tests across `IconChest.Core.Tests` and `IconChest.Api.Tests`) or target the Phase 5 query suite specifically:

bash
# Run all query language unit, semantic, fuzz, date, golden & benchmark tests (203 tests)
dotnet test tests/IconChest.Core.Tests --filter "FullyQualifiedName~Query"
# Run all API query endpoint integration tests
dotnet test tests/IconChest.Api.Tests --filter "FullyQualifiedName~Query"
# Run the complete test suite across the entire solution
dotnet test

---

### **2. Test via the CLI (**`**src/IconChest.Cli**`**)**

The CLI provides subcommands under `query` for validating syntax/semantics, inspecting AST and SQLite query plans, executing queries, and verifying generated docs.

#### **A. Validate valid expressions and inspect compiled SQL**

bash
# Validate and print compiled parameterised SQL WHERE clause and bindings
dotnet run --project src/IconChest.Cli -- query validate "tag == \"animal\" and (width > 64 or transparent == true)" --sql

#### **B. Validate error reporting and terminal caret placement**

bash
# 1. Syntax error: missing value after operator
dotnet run --project src/IconChest.Cli -- query validate "tag =="
# 2. Semantic error: unknown field with typo suggestion
dotnet run --project src/IconChest.Cli -- query validate "fomrat == png"
# 3. Semantic error: invalid size unit / rating range
dotnet run --project src/IconChest.Cli -- query validate "rating > 10 and size < 5foo"

#### **C. Inspect the AST and SQLite** `**EXPLAIN QUERY PLAN**`

bash
# Dumps untyped AST JSON, compiled SQL with parameters, and SQLite's query plan
dotnet run --project src/IconChest.Cli -- query explain "tag == \"dog\" or type == png"

#### **D. Run queries against your local library database**

bash
# Search images using query language
dotnet run --project src/IconChest.Cli -- query run "type in (png, svg) and rating >= 3" --limit 10

#### **E. Verify / update documentation**

bash
# Verify the markdown docs match the codebase definitions
dotnet run --project src/IconChest.Cli -- query docs --write

---

### **3. Test via the HTTP API (**`**src/IconChest.Api**`**)**

Start the API server (or test with `curl` against a running instance):

bash
dotnet run --project src/IconChest.Api -- --port 5000 --swagger

#### **A. Query Images (**`**GET /api/images?q=...**`**)**

bash
# Search using the query language
curl -s "http://127.0.0.1:5000/api/images?q=type%20==%20png%20and%20width%20%3E=%20128" | jq
# Retrieve only IDs matching query
curl -s "http://127.0.0.1:5000/api/images/ids?q=tag%20like%20\"*icon*\"" | jq

#### **B. Verify 400 ProblemDetails on invalid query**

bash
# Query with syntax error returns RFC 7807 problem details with positional queryErrors
curl -s "http://127.0.0.1:5000/api/images?q=tag%20==" | jq

Expected response:

json
{
  "type": "https://tools.ietf.org/html/rfc7807#status-400",
  "title": "Invalid query",
  "status": 400,
  "detail": "The query contains 1 syntax or semantic error(s).",
  "queryErrors": [
    {
      "code": "expected_value",
      "message": "Expected value after '=='.",
      "position": 6,
      "length": 0
    }
  ]
}

#### **C. Mutual exclusivity test (reject** `**q**` **+ structured filters)**

bash
# Mixing `q` and structured filter returns 400
curl -s "http://127.0.0.1:5000/api/images?q=rating%20%3E%203&type=png" | jq

#### **D. Standalone Validation Endpoint (**`**POST /api/query/validate**`**)**

bash
curl -s -X POST http://127.0.0.1:5000/api/query/validate \
  -H "Content-Type: application/json" \
  -d '{"text": "width > 128 and tag like \"app*\""}' | jq

#### **E. Help & Examples Endpoint (**`**GET /api/query/help**`**)**

bash
# Returns all 20 fields, operators, aliases, and 30+ examples
curl -s http://127.0.0.1:5000/api/query/help | jq

#### **F. Convert Simple Filter to Query (**`**POST /api/query/from-filter**`**)**

bash
curl -s -X POST http://127.0.0.1:5000/api/query/from-filter \
  -H "Content-Type: application/json" \
  -d '{"types": ["png", "svg"], "minRating": 4, "text": "dashboard"}' | jq