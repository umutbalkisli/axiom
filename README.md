# Axiom

Axiom is an API test platform and it's designed to write tests without using a programming language. Test scenarios are stored as YAML files so they can be versioned in Git and executed consistently in CI/CD. For advanced users it also supports SQL queries.

## What is implemented now

- .NET 10 backend engine with YAML parsing
- CLI to run collections (`run`) and a local HTTP host (`serve`) used by the desktop app
- Parallel test-case execution with a bounded channel for backpressure control
- HTTP request steps and DB query steps
- SQLite and SQL Server support
- Collection-level and test-level variables, with `{{variable}}` templating
- Assertion engine with JSON path support, and a human-readable stdout report (or `--json` output)
- OpenAPI import: generates one basic scenario per endpoint
- Electron + React desktop app:
  - open or create a collection folder
  - import a collection from an OpenAPI/Swagger URL
  - edit collection variables and run settings
  - create, edit and delete test scenarios with a visual builder
  - run the collection and view the report
  - English and Turkish UI, light/dark/system theme

## Project structure

```
backend/
  Axiom.slnx
  src/Axiom/
    Models/       YAML contracts (collection, test case, step, assertion, ...)
    Documents/    DTOs exchanged with the desktop app
    Parsing/      YAML collection loader
    Runtime/      test-case executor, collection runner, assertion engine,
                  template resolver, DB query executor
    Services/     AxiomService, collection management, host server, report formatter
    ProgramEntry.cs   CLI entry point
desktop/          Electron shell + Vite/React renderer
samples/          local sample collections (git-ignored)
```

## Requirements

- .NET SDK 10
- Node.js and npm (desktop app only)

## CLI usage

From the repository root:

```bash
dotnet run --project backend/src/Axiom -- run <collection-folder>
dotnet run --project backend/src/Axiom -- run <collection-folder> --json
dotnet run --project backend/src/Axiom -- serve [--port <number>]
```

- `run` executes every `tests/**/*.test.yaml` in the collection and prints a report. With `--json` the result is printed as a JSON envelope (`{ ok, data, error }`).
- `serve` starts the local HTTP host on `127.0.0.1` (default port `50743`). The desktop app starts this itself.

Exit codes for `run`:

- `0`: all tests passed
- `2`: one or more tests failed
- `3`: runtime failure (invalid config, connectivity issue, etc.)

## Local host API

Used by the desktop app (via Electron IPC). All collection endpoints take a `folderPath` query parameter.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/health` | Health check |
| GET | `/api/collection` | Read collection |
| POST | `/api/collection/init` | Create an empty collection |
| POST | `/api/collection/variables` | Save collection variables |
| POST | `/api/collection/settings` | Save run settings |
| POST | `/api/collection/import-openapi` | Import scenarios from an OpenAPI URL |
| GET | `/api/tests` | List test scenarios |
| GET | `/api/tests/{fileName}` | Read one scenario |
| POST | `/api/tests` | Create or update a scenario |
| DELETE | `/api/tests/{fileName}` | Delete a scenario |
| POST | `/api/run` | Run the collection and return the report |

## Collection layout

```
my-collection/
  collection.yaml
  tests/
    get-one-todo.test.yaml
    ...
```

## YAML format (v0)

Collection file: `collection.yaml`

```yaml
name: Todos API
description: Deterministic sample collection
connections:
  todos_db:
    provider: sqlite
    connection_string: Data Source=../data/todos.db;
variables:
  base_url: https://jsonplaceholder.typicode.com/todos
run_settings:
  max_parallel_test_cases: 2   # default 4
  step_timeout_seconds: 30     # default 30
request_defaults:
  headers:
    Accept: application/json
```

Relative SQLite `Data Source` paths are resolved against the collection folder.

Test file: `tests/*.test.yaml`

```yaml
name: Get one todo
description: Read ID from DB then call API
variables:
  expected_status: 200
steps:
  - id: get_id
    type: db_query
    connection: todos_db
    sql: "SELECT Id FROM todos LIMIT 1"
    save_as: todo_id
    assert:
      - source: row_count
        operator: ">"
        expected: 0

  - id: get_todo
    type: request
    method: GET
    url: "{{base_url}}/{{todo_id.0.Id}}"
    query_params: {}
    headers: {}
    assert:
      - source: status
        operator: "=="
        expected: "{{expected_status}}"
      - source: body
        path: title
        operator: exists
```

Step types: `request` (`method`, `url`, `query_params`, `headers`, `body`) and `db_query` (`connection`, `sql`, `save_as`).

Each step also stores its results in the context as `<step_id>_status`, `<step_id>_duration_ms`, `<step_id>_response_text`, `<step_id>_response_json` (request) and `<step_id>_rows`, `<step_id>_row_count` (DB), so later steps can reference them.

## Assertion sources and operators

Sources:

- Request step: `status`, `duration_ms`, `body` (parsed JSON when possible)
- DB step: `row_count`, `duration_ms`, `rows`
- Any context variable name can also be referenced

Use `path` (dot-separated, e.g. `items.0.name`) to pick a value out of a source.

Operators:

- `==`, `!=`, `>`, `>=`, `<`, `<=`, `contains`, `not_contains`, `exists`, `not_exists`

String comparisons are case-insensitive, and numeric values are compared numerically.

## Electron desktop

```bash
cd desktop
npm install
npm start
```

`npm start` builds the React renderer with Vite and launches Electron, which spawns `dotnet run` on the backend project to start the local host. The .NET SDK must be installed and discoverable (common install paths are checked, otherwise `dotnet` from `PATH` is used).

Other scripts: `npm run build` (renderer only) and `npm run format` (Prettier).

## Next build targets

1. JSON path / response schema assertion builder in UI
2. Rich report screen with trends and failed-step diagnostics
3. Plugin-based connectors and secure secret management
4. Packaging for Windows/macOS installers
