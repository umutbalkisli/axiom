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
  - create, edit, reorder and delete test steps with a visual builder (unsaved-changes tracking, collapsible steps)
  - run the collection and see, per test, which step and assertion failed and why (expected vs actual)
  - reopens the last collection on launch, with a recent-collections list
  - English and Turkish UI, light/dark/system theme

## Project structure

```
backend/
  Axiom.slnx
  src/Axiom.Core/     engine library (no ASP.NET dependency)
    Models/           YAML contracts (collection, test case, step, assertion, ...)
    Documents/        DTOs exchanged with the desktop app
    Parsing/          YAML collection loader, collection folder layout
    Runtime/          collection runner, test-case executor, step executors,
                      assertion engine and operators, DB query executor,
                      template resolver
    Validation/       save-time validation of tests and collection settings
    Secrets/          secret providers (env, file, k8s, vault, local), resolver, output masking
    Services/         collection management, collection initializer, OpenAPI importer
    AxiomServiceCollectionExtensions.cs   AddAxiomCore() DI registration
  src/Axiom/          executable: CLI (`run`, `serve`) and local HTTP host
    Hosting/          HTTP endpoints used by the desktop app
    Services/         stdout report formatter
desktop/              Electron shell + Vite/React renderer
samples/              local sample collections (git-ignored)
```

## Extending the engine

Everything below is registered through DI in `AddAxiomCore()`; add your own registration instead of editing existing classes.

| To add... | Implement | Notes |
| --- | --- | --- |
| A new step `type` | `IStepExecutor` (+ `IStepValidator` for save-time checks) | `TestCaseExecutor` picks it up by `Type` |
| A new assertion operator | `IAssertionOperator` | Usable as `operator:` in YAML |
| A new assertion aggregation | `IAssertionAggregation` | Usable as `aggregate:` in YAML and listed in the builder dropdown |
| A new database provider | `IDbConnectionFactory` | Usable as `provider:` in a connection |
| A new secret store (vault, cloud KMS, ...) | `ISecretProvider` | Usable as `provider:` in `secrets:` |

## Requirements

- .NET SDK 10
- Node.js and npm (desktop app only)

## CLI usage

From the repository root:

```bash
dotnet run --project backend/src/Axiom -- run <collection-folder>
dotnet run --project backend/src/Axiom -- run <collection-folder> --env ci
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
| POST | `/api/collection/settings` | Save variables, connections and secret references |
| POST | `/api/collection/secrets` | Save secret references only |
| GET | `/api/secrets/providers` | List secret providers and their key formats |
| POST | `/api/collection/import-openapi` | Import scenarios from an OpenAPI URL |
| GET | `/api/tests` | List test scenarios |
| GET | `/api/tests/{fileName}` | Read one scenario |
| POST | `/api/tests` | Create or update a scenario |
| DELETE | `/api/tests/{fileName}` | Delete a scenario |
| POST | `/api/run` | Run the collection and return the report (optional body: `localSecrets`, `environment`) |

## Collection layout

```
my-collection/
  collection.yaml
  tests/
    get-one-todo.test.yaml
    ...
```

### Test file names

The test's real name lives inside the file (`name:`); the file name is only a short, readable handle.

- A new test gets a slug of its name: lowercase ASCII (`Şifre Değiştir` becomes `sifre-degistir`), at most 48 characters, cut on a word boundary. A long name never makes a long file name.
- Names never collide: if the file exists, a suffix is added (`get-one-todo-2`). Saving a new test can no longer overwrite another one.
- Renaming a test renames its file to match, as long as the file still has the name Axiom generated. If you renamed the file yourself, Axiom leaves it alone.
- Re-importing an OpenAPI document skips operations that were already imported, so edited tests are not overwritten.

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

Variable names (collection and test `variables`, and `save_as`) may contain only letters and underscores (`base_url`, `todo_id`); `secret` is reserved. A collection that breaks this rule fails to load with the offending file and name, and the desktop app rejects such names when saving.

Each step also stores its results in the context as `<step_id>_status`, `<step_id>_duration_ms`, `<step_id>_response_text`, `<step_id>_response_json` (request) and `<step_id>_rows`, `<step_id>_row_count` (DB), so later steps can reference them.

## Secrets

Never put passwords or tokens in `collection.yaml`; it lives in Git. Declare a **reference** instead and use `{{secret.<name>}}` wherever a value is needed (variables, headers, URLs, bodies, SQL, connection strings, assertion values):

```yaml
secrets:
  api_token: { provider: env,   key: API_TOKEN }
  db_password: { provider: vault, key: "secret/myapp/db#password" }
  webhook_key: { provider: k8s,   key: "my-secret/webhook-key" }
variables:
  auth_header: "Bearer {{secret.api_token}}"
connections:
  orders_db:
    provider: sqlserver
    connection_string: "Server=db;Database=orders;User Id=app;Password={{secret.db_password}}"
```

### Different sources per environment

The same secret often comes from different places: a local value on a laptop, environment variables in CI, a Kubernetes secret in a cluster. The `provider`/`key` pair is the default source; `environments` overrides it for a named environment:

```yaml
secrets:
  db_password:
    provider: local            # default: value kept on this machine (desktop)
    key: db_password
    environments:
      ci:   { provider: env, key: DB_PASSWORD }
      prod: { provider: k8s, key: "orders-db/password" }
```

Select the environment with `axiom run <folder> --env ci` (or `AXIOM_ENVIRONMENT=ci`), or with the environment dropdown next to **Run** in the desktop app (shown once any secret has an override). A secret with no override for the selected environment uses its default source, and environment names are case-insensitive.

All declared secrets are read before a run starts; if any cannot be read the run fails with a message naming the secret and provider (never a value).

| Provider | `key` format | Configuration (environment variables) |
| --- | --- | --- |
| `env` | `ENV_VAR_NAME` | none. Best for CI/CD |
| `file` | `relative/file/name` | `AXIOM_SECRETS_DIR`. Works with mounted Kubernetes secret volumes and Docker secrets; keys cannot escape the directory |
| `k8s` | `secret-name/data-key` or `namespace/secret-name/data-key` | In a pod it uses the service account (needs `get` on the secret). Elsewhere set `AXIOM_K8S_API_URL` (+ `AXIOM_K8S_TOKEN`); `AXIOM_K8S_NAMESPACE` sets the default namespace |
| `vault` | `mount/path#field` | `VAULT_ADDR`, `VAULT_TOKEN`, optional `VAULT_NAMESPACE`; KV v2 by default, `AXIOM_VAULT_KV_VERSION=1` for KV v1 |
| `local` | secret name | Values are supplied by the caller for a single run. The desktop app keeps them encrypted with the OS secure storage (Keychain / DPAPI / libsecret) outside the collection folder and never shows them again |

Provider connection settings come only from the environment, never from collection files, so a shared collection cannot redirect secret lookups. Any secret value of 4+ characters is replaced with `********` in reports and JSON output. To add another store, implement `ISecretProvider` and register it (see "Extending the engine").

## Assertion sources and operators

Sources:

- Request step: `status`, `duration_ms`, `body` (parsed JSON when possible)
- DB step: `row_count`, `duration_ms`, `rows`
- Any context variable name can also be referenced

In the desktop builder, source and path are typed as one expression, e.g. `body.items.*.price` (the first segment is the source, the rest is the path); the YAML keeps them as separate `source` and `path` fields.

Use `path` (dot-separated, e.g. `items.0.name`) to pick a value out of a source.

Use `*` in a path to collect a field from every item of a list (or every property of an object): `items.*.price` gives the list of all prices, and `items.*.tags.*` flattens nested lists. Items where the rest of the path finds nothing are skipped.

Add `aggregate` to compare a computed value instead of the value itself:

| `aggregate` | Result |
| --- | --- |
| `count` | number of items in a list or rows, or properties in an object |
| `sum`, `avg`, `min`, `max` | computed over a list of numbers |

```yaml
- source: body
  path: items
  aggregate: count
  operator: ">="
  expected: 1
- source: body
  path: items.*.price
  aggregate: sum
  operator: "<"
  expected: 500
- source: rows          # DB step
  path: "*.amount"
  aggregate: max
  operator: "<="
  expected: 1000
```

Aggregations fail with a clear message instead of guessing: a missing value or a non-list can't be aggregated, `sum` over non-numbers fails, and `avg`/`min`/`max` of an empty list fail (`sum` of an empty list is 0). Aggregation names are case-insensitive and extensible via `IAssertionAggregation`. In the desktop builder it is the aggregation dropdown after the path field.

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
3. Plugin-based connectors (load extra providers, step types and operators from a plugins folder)
4. Packaging for Windows/macOS installers
