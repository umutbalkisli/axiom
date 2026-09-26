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
  - **Send** a single step while building a test: the steps up to it run as they are in the editor (saved or not), and its response (status, headers, JSON body) or SQL rows are shown; click any value to add a check for it
  - run the whole collection, one test, or only the tests that failed last time; results appear as each test finishes, and a run can be cancelled
  - see, per test, which step and assertion failed and why (expected vs actual)
  - reopens the last collection on launch, with a recent-collections list
  - English and Turkish UI, light/dark/system theme

## Project structure

```
backend/
  Axiom.slnx
  tests/Axiom.Tests/  xUnit tests for the engine and the host
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
| A new step `type` | `IStepExecutor` (+ `IStepValidator` for save-time checks) | `StepRunner` picks it up by `Type` |
| A new assertion operator | `IAssertionOperator` | Usable as `operator:` in YAML |
| A new assertion aggregation | `IAssertionAggregation` | Usable as `aggregate:` in YAML and listed in the builder dropdown |
| A new database provider | `IDbConnectionFactory` | Usable as `provider:` in a connection |
| A new secret store (vault, cloud KMS, ...) | `ISecretProvider` | Usable as `provider:` in `secrets:` |

## Tests

```bash
dotnet test backend/tests/Axiom.Tests
```

The xUnit suite (about 280 tests, a few seconds) covers the assertion engine (every operator, option, aggregation and outcome), template and path resolution, secrets and their providers (Vault and Kubernetes against a local fake server), the YAML loader, file naming, validation, the collection management service, shared steps (including run-once sharing across parallel tests and cycle detection), the request and database step executors, and whole-collection runs through the real dependency injection setup with a stubbed HTTP handler. No test needs network access. The public API of `Axiom.Core` is documented with XML comments.

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
- `serve` starts the local HTTP host on `127.0.0.1` (default port `50743`; `--port 0` picks a free one). The desktop app starts this itself.

Exit codes for `run`:

- `0`: all tests passed
- `2`: one or more tests failed
- `3`: runtime failure (invalid config, connectivity issue, etc.)

### Failed versus could not be evaluated

Reports tell apart two different things that both stop a test from passing:

- **Failed**: the check ran and the API did not behave as expected (`Expected '==' with value '404', actual '200'`). If the path did not exist at all, the message says so.
- **Error** (`ERROR` in the text report, `outcome: "Error"` in `--json`): the check or step could not be evaluated, which points at the test or its environment rather than the API: an unknown operator, aggregation or source, an invalid regular expression, an unresolved `{{variable}}`, ordering a list, an unreachable server. Each assertion, step and test has an `outcome` of `Passed`, `Failed` or `Error`; a test with any error counts as an error.

The summary line and the JSON result count both (`failedCount`, `errorCount`), and the desktop app shows errors in amber apart from failures in red. The exit code is `2` when anything did not pass, failed or error.

## Local host API

Used by the desktop app (via Electron IPC). All collection endpoints take a `folderPath` query parameter.

Every request must carry `Authorization: Bearer <token>`; anything else gets `401`. The API reads and writes files and runs tests, so neither another program on the machine nor a web page open in a browser may call it. The token comes from the `AXIOM_HOST_TOKEN` environment variable: the desktop app generates a new one at every launch and starts the host on a free port. Started by hand without it, `serve` generates a token and prints it (`AXIOM_HOST_TOKEN <token>`).

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
| POST | `/api/tests/preview` | Run an unsaved test's steps up to one of them and return what that step received (body: `test`, `stepIndex`, optional `localSecrets`, `environment`) |
| POST | `/api/run` | Run the collection (optional body: `localSecrets`, `environment`, `tests` to run only some); streams progress, see below |

`/api/run` answers with newline-delimited JSON (`application/x-ndjson`), one event per line as it happens: `{"type":"started","total":N}`, then `{"type":"test","test":{...}}` as each test finishes, then `{"type":"completed","exitCode":0,"report":"...","result":{...}}`, or `{"type":"failed","message":"..."}` if the run could not start. Closing the request cancels the run.

## Collection layout

```
my-collection/
  collection.yaml
  shared/            reusable step groups (optional)
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

## Shared steps

Steps that many tests need first (get an auth token, read an id from the database) can be written once and reused. A shared group is a file in `shared/`:

```yaml
# shared/get-auth-token.shared.yaml
name: Get auth token
run: once            # once per run, or `each` (default): inside every test that uses it
steps:
  - id: login
    type: request
    method: POST
    url: "{{base_url}}/token"
    body: '{ "user": "{{secret.user}}", "password": "{{secret.password}}" }'
    save_as: token_resp
```

A test uses it with an `include` step; variables the group saves are then available to the steps after it:

```yaml
steps:
  - id: auth
    type: include
    ref: get-auth-token        # the shared file name without `.shared.yaml`
  - id: get_orders
    type: request
    method: GET
    url: "{{base_url}}/orders"
    headers:
      Authorization: "Bearer {{token_resp.token}}"
```

- `run: once` executes the group a single time per run, even when many tests run in parallel; every including test gets its variables and results. It starts from the collection variables and secrets only, so it cannot depend on one test's own variables. If it fails, every test that includes it fails with the same error.
- `run: each` runs the steps again inside each test, with that test's variables.
- Groups can include other groups. A cycle, or a `ref` that does not exist, is reported instead of hanging.
- A group's file name stays fixed once created, because tests refer to it. A group that is still included cannot be deleted.
- In the desktop app: **Collection → Shared steps** to create groups, then **Use shared steps** in the test builder. Results show the shared steps nested under the include step.

## YAML format (v0)

Keys are checked when a collection is loaded for a run: a key Axiom does not know is an error, not something silently skipped, so a misspelled `asert:` cannot turn into a test that checks nothing. The message names the file, the line and the likely intended key:

```
tests/get-user.test.yaml (line 7, column 3): unknown key 'asert' in a step. Did you mean 'assert'? Valid keys: id, type, name, ...
```

Values inserted by templates (`{{variable}}`) are written the same way on every machine: `1.5` stays `1.5` on a system set to Turkish or German.

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

Database connections are pooled per connection string for the length of a run, so tests running in parallel query side by side instead of waiting for each other. A private in-memory SQLite database (`Data Source=:memory:`) exists only inside one connection, so its queries share a single connection and take turns.

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

Step types: `request` (`method`, `url`, `query_params`, `headers`, `body`), `db_query` (`connection`, `sql`, `save_as`) and `include` (`ref`, see Shared steps).

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

- Request step: `status`, `duration_ms`, `body` (parsed JSON when possible, otherwise text), `body_text` (the raw response text), `headers` (response headers, looked up case-insensitively: `source: headers`, `path: content-type`)
- DB step: `row_count`, `duration_ms`, `rows`
- Any context variable name can also be referenced

In the desktop builder, source and path are typed as one expression, e.g. `body.items.*.price` (the first segment is the source, the rest is the path); the YAML keeps them as separate `source` and `path` fields.

A step's **saved result** (`save_as: my_response`) works as a source and in templates. Its plain names are the fields of the response body: `my_response.items.0.name`. The HTTP response itself lives under `@http`, which can never clash with a body field:

| Path | Value |
| --- | --- |
| `my_response.@http.status` | the HTTP status code |
| `my_response.@http.headers.content-type` | a response header (names are case-insensitive) |
| `my_response.@http.duration_ms` | how long the request took |
| `my_response.@http.body_text` | the raw response text |

Templates work the same way: `{{my_response.@http.headers.x-request-id}}` in a URL, header or body. In YAML, quote a path that starts with `@` (`path: "@http.status"`); the builder does this for you. Inside the same step, the plain `status`, `headers` and `body_text` sources are also available.

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

| Group | Operators |
| --- | --- |
| Compare | `==`, `!=`, `>`, `>=`, `<`, `<=`, `approx` (within `tolerance` of the expected number) |
| Text | `contains`, `not_contains`, `starts_with`, `ends_with`, `matches` (regular expression, found anywhere in the text; anchor with `^` and `$`) |
| List | `in`, `not_in` (expected is a list: `[200, 201]`, JSON text, or `200, 201`) |
| Presence and type | `exists` (has a non-null value), `not_exists`, `is_null` (present and null), `is_missing` (the path does not exist), `is_empty`, `is_not_empty`, `is_type` (`string`, `number`, `boolean`, `array`, `object`, `null`) |

Unknown operators are rejected when a test is saved.

Per-assertion options change how the comparison behaves:

```yaml
- source: body
  path: code
  operator: "=="
  expected: "200"
  strict: true          # no type coercion: the text "200" is not the number 200
- source: body
  path: name
  operator: "=="
  expected: Ann
  case_sensitive: true  # default: text comparisons ignore case
- source: body
  path: price
  operator: approx
  expected: 10
  tolerance: 0.05
```

String comparisons are case-insensitive, and numeric values are compared numerically.

How values are compared:

- `==` / `!=` on lists and objects compare them structurally (key order and whitespace don't matter). The expected value may be JSON text, e.g. `{"a":[1,2]}`.
- `contains` / `not_contains`: text contains the text; a list contains an item that does (`[1,5]` does not contain `15`); an object contains a key or a value that does. On a whole response `body` (no `path`) the raw response text is searched, which is fast even for very large responses. Use `body_text` to search the raw text of a path-less body explicitly.
- `>`, `>=`, `<`, `<=` need single values; use an aggregation (such as `count`) for lists.
- Failure messages show a short preview of large values, while the result still keeps the complete actual and expected values.
- A response body is only parsed as JSON when an assertion or a later step reads it, so status-only checks on large responses are cheap.

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
