# Axiom

Axiom is an API test platform and it's designed to write tests without using a programming language. Test scenarios are stored as YAML files so they can be versioned in Git and executed consistently in CI/CD. For advanced users it also supports SQL queries.

## See it in action

https://github.com/user-attachments/assets/ba9556b1-94f8-465e-90f9-8951f2a48f16



## What is implemented now

- .NET 10 backend engine with YAML parsing
- One executable, `axiom`: the CLI (`run`), the app (`ui`, a web UI built into the executable) and a headless API (`serve`)
- Parallel test-case execution with a bounded channel for backpressure control
- HTTP request steps and DB query steps
- SQLite and SQL Server support
- Collection-level and test-level variables, with `{{variable}}` templating
- Assertion engine with JSON path support, and a human-readable stdout report (or `--json` output)
- OpenAPI import: generates one basic scenario per endpoint
- The app (`axiom ui`, React, opened in an app window of Edge or Chrome):
  - open or create a collection folder
  - import a collection from an OpenAPI/Swagger URL
  - edit collection variables and run settings
  - create, edit, reorder and delete test steps with a visual builder (unsaved-changes tracking, collapsible steps)
  - right-click a test (or **Clone** in the builder) to copy it as a starting point for a variation: the file is copied as it is, comments included, and only its name changes (`Get user (copy)`)
  - organise tests in folders: group the sidebar and test list by folder or by endpoint, drag a test onto a folder or use **Move to folder…**, and right-click a folder to create a test in it, run it, rename it or delete it; results are grouped by folder too, with passing folders collapsed
  - **Send** a single step while building a test: the steps up to it run as they are in the editor (saved or not), and it shows what was sent (method, full URL, headers and body, or the SQL; opened by itself when the call fails, with **Copy as cURL**) and what came back (status, headers, JSON body, or SQL rows); click any value to add a check for it
  - run the whole collection, one test, or only the tests that failed last time; results appear as each test finishes, and a run can be cancelled
  - see, per test, which step and assertion failed and why (expected vs actual)
  - reopens the last collection on launch, with a recent-collections list; right-click menus and a folder picker built in
  - English and Turkish UI, light/dark/system theme

## Project structure

```
backend/
  Axiom.slnx
  tests/Axiom.Tests/  xUnit tests for the engine and the host
  src/Axiom.Core/     engine library (no ASP.NET dependency)
    Models/           YAML contracts (collection, test case, step, assertion, ...)
    Documents/        DTOs exchanged with the app
    Parsing/          YAML collection loader, collection folder layout
    Runtime/          collection runner, test-case executor, step executors,
                      assertion engine and operators, DB query executor,
                      template resolver
    Validation/       save-time validation of tests and collection settings
    Secrets/          secret providers (env, file, k8s, vault, local), resolver, output masking
    Services/         collection management, collection initializer, OpenAPI importer
    AxiomServiceCollectionExtensions.cs   AddAxiomCore() DI registration
  src/Axiom/          the `axiom` executable: CLI (`run`, `ui`, `serve`) and local HTTP host
    Hosting/          HTTP endpoints, session cookie and security checks, serving the web UI,
                      folder browser, preferences, opening the app window
    LocalSecrets/     local secret values in the OS secure storage (DPAPI, Keychain, libsecret)
    Services/         stdout report formatter
ui/                   Vite/React web UI; its build (ui/dist) is embedded in the executable
samples/              sample collections
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
- Node.js and npm (to build the UI)

Users of a published `axiom` executable need neither: it is self-contained.

## CLI usage

From the repository root:

```bash
dotnet run --project backend/src/Axiom -- run <collection-folder>
dotnet run --project backend/src/Axiom -- run <collection-folder> --env ci
dotnet run --project backend/src/Axiom -- run <collection-folder> --json
dotnet run --project backend/src/Axiom -- serve [--port <number>]
dotnet run --project backend/src/Axiom -- network <url>
```

- `run` executes every `tests/**/*.test.yaml` in the collection and prints a report. With `--json` the result is printed as a JSON envelope (`{ ok, data, error }`).
- `serve` starts the API alone (no UI) on `127.0.0.1` (default port `50743`; `--port 0` picks a free one), for other tools. `ui` starts the app (see [The app](#the-app)).
- A secret whose provider is `local` is read from this machine's secure storage, where the app keeps it, so `axiom run` works on the machine you set the collection up on. On a build server nothing is stored there: use an environment override (`--env ci`, see [Secrets](#secrets)).

Exit codes for `run`:

- `0`: all tests passed
- `2`: one or more tests failed
- `3`: runtime failure (invalid config, connectivity issue, etc.)

### Failed versus could not be evaluated

Reports tell apart two different things that both stop a test from passing:

- **Failed**: the check ran and the API did not behave as expected (`Expected '==' with value '404', actual '200'`). If the path did not exist at all, the message says so.
- **Error** (`ERROR` in the text report, `outcome: "Error"` in `--json`): the check or step could not be evaluated, which points at the test or its environment rather than the API: an unknown operator, aggregation or source, an invalid regular expression, an unresolved `{{variable}}`, ordering a list, an unreachable server. Each assertion, step and test has an `outcome` of `Passed`, `Failed` or `Error`; a test with any error counts as an error.

The summary line and the JSON result count both (`failedCount`, `errorCount`), and the app shows errors in amber apart from failures in red. The exit code is `2` when anything did not pass, failed or error.

## Networking and proxies

Every call Axiom makes to the outside (request steps, OpenAPI import, Vault) goes through one set of network settings, read from the environment, so the same collection runs on a laptop behind a company proxy and on a build server. The app's own window only talks to `127.0.0.1`, which never goes through a proxy.

| Setting | What it does |
| --- | --- |
| *(nothing set)* | The system's proxy settings: on Windows the Internet Options / Edge settings, including an automatic configuration (PAC) script; on macOS System Settings → Network → Proxies. |
| `HTTPS_PROXY`, `HTTP_PROXY`, `ALL_PROXY` | A proxy for https / http addresses (in upper or lower case), used instead of the system's settings. |
| `NO_PROXY` | Addresses that go direct: comma-separated; `corp.com` and `.corp.com` also cover subdomains (as in curl), `*` covers everything, and `localhost` / `127.0.0.1` always go direct. |
| `AXIOM_PROXY` | Overrides all of the above: `system` (the default), `none` (always direct, for a network without a proxy), or an address such as `http://proxy.corp:8080` (with `NO_PROXY` for exceptions). |
| `AXIOM_CA_CERTS` | A PEM file of extra root certificates to trust, for a company proxy that inspects HTTPS with its own certificate. The system's roots stay trusted; a certificate for the wrong host name or an expired one is still refused. |

**Signing in to the proxy.** When a proxy asks, Axiom sends the signed-in user's credentials (Windows NTLM / Kerberos) by itself; nothing to configure on a company Windows laptop. A proxy that wants a user name and password gets them from its address: `HTTPS_PROXY=http://user:password@proxy.corp:8080` (or `AXIOM_PROXY`); special characters in the password are URL-encoded (`@` as `%40`). A password is never shown: messages and `axiom network` print `user:***@proxy`.

**When a request fails,** the step says which way it went (direct, or through which proxy) and what usually fixes it: a proxy asking for a sign-in (407, reported as the proxy's answer, not as a failed check), a certificate the machine does not trust (HTTPS inspection: `AXIOM_CA_CERTS`), a proxy that cannot be reached, or a name that cannot be found.

**`axiom network <url>`** shows how Axiom reaches an address and tries it: the proxy setting and where it came from, the route (direct or through which proxy), the sign-in, extra trusted certificates, the proxy variables that are set, and the result or the explained error. It is the first thing to run when requests fail behind a proxy; `--json` gives the same as data.

```
URL            : https://api.example.com/health
Proxy setting  : system settings (the system's proxy settings, including a PAC script)
Route          : through proxy http://proxy.corp:8080
Proxy sign-in  : the signed-in user (Windows NTLM / Kerberos), or the user name and password in the proxy address
Extra roots    : none (AXIOM_CA_CERTS not set)
Result         : HTTP 200 in 142 ms. The network path works.
```

## Local host API

Used by the web UI (and available to other tools through `serve`). All collection endpoints take a `folderPath` query parameter.

The API reads and writes files and runs tests, so neither another program on the machine nor a web page open in a browser may call it:

- Every request needs the host's token: as `Authorization: Bearer <token>`, or, for the UI, as its session cookie. Anything else gets `401`.
- `axiom ui` makes a new token at every launch and opens the UI with a one-time link (`/?token=...`). The host swaps it for an `HttpOnly`, `SameSite=Strict` cookie (pages of other sites never send it) and redirects, so the token leaves the address bar.
- Only requests addressed to `127.0.0.1`, `localhost` or `[::1]` are served (`421` otherwise), which stops a web page that points its own domain name at this machine (DNS rebinding).
- `serve` takes its token from `AXIOM_HOST_TOKEN`, or generates one and prints it (`AXIOM_HOST_TOKEN <token>`).

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
| GET | `/api/tests/{path}` | Read one scenario; `{path}` is relative to `tests/` and may contain `/` (`orders/create.test.yaml`) |
| POST | `/api/tests` | Create or update a scenario (a new one in `folder`, when given) |
| POST | `/api/tests/clone` | Copy a scenario in its folder under a new name (body: `fileName`, `name`); only `name:` and `id:` change |
| POST | `/api/tests/move` | Move a scenario to another folder as it is (body: `fileName`, `folder`; empty folder is the top level) |
| POST | `/api/folders/rename` | Rename or move a folder with its tests (body: `folder`, `newFolder`); refused if the target exists |
| POST | `/api/folders/delete` | Delete the tests in a folder, keeping other files (body: `folder`) |
| DELETE | `/api/tests/{path}` | Delete a scenario |
| POST | `/api/tests/preview` | Run an unsaved test's steps up to one of them and return what that step received (body: `test`, `stepIndex`, optional `localSecrets`, `environment`) |
| GET | `/api/local-secrets` | Names of the locally stored secrets of a collection (values are never returned) |
| POST | `/api/local-secrets` | Store a local secret's value in the OS secure storage (body: `name`, `value`) |
| DELETE | `/api/local-secrets/{name}` | Remove a local secret |
| GET | `/api/fs/list` | A folder's subfolders, for the folder picker (`path`; default: home), marking collections |
| GET | `/api/fs/check` | Whether a folder exists and holds a collection (`path`) |
| POST | `/api/fs/mkdir` | Create a folder (body: `parent`, `name`) |
| GET, POST | `/api/preferences` | The UI's preferences (POST merges; a `null` removes a key) |
| POST | `/api/session/ping`, `/api/session/goodbye` | The UI is still open / its window is closing (see [The app](#the-app)) |
| POST | `/api/run` | Run the collection (optional body: `localSecrets`, `environment`, `tests` to run only some: paths, ids, or `orders/` for a whole folder); streams progress, see below |

`/api/run` answers with newline-delimited JSON (`application/x-ndjson`), one event per line as it happens: `{"type":"started","total":N}`, then `{"type":"test","test":{...}}` as each test finishes, then `{"type":"completed","exitCode":0,"report":"...","result":{...}}`, or `{"type":"failed","message":"..."}` if the run could not start. Closing the request cancels the run.

## Collection layout

```
my-collection/
  collection.yaml
  shared/            reusable step groups (optional; flat, tests refer to them by name)
  tests/
    get-one-todo.test.yaml
    orders/          folders group tests (up to 3 deep)
      create-order.test.yaml
      refunds/
        full-refund.test.yaml
```

A test is addressed everywhere by its path relative to `tests/`, with `/` on every system: `orders/create-order.test.yaml`. The same file name can exist in different folders.

### Test file names

The test's real name lives inside the file (`name:`); the file name is only a short, readable handle.

- A new test gets a slug of its name: lowercase ASCII (`Şifre Değiştir` becomes `sifre-degistir`), at most 48 characters, cut on a word boundary. A long name never makes a long file name.
- Names never collide: if the name is taken in that folder (in any letter case), a suffix is added (`get-one-todo-2`).
- Renaming a test renames its file to match (in the same folder), as long as the file still has the name Axiom generated. If you renamed the file yourself, Axiom leaves it alone.
- Re-importing an OpenAPI document skips operations that were already imported, so edited tests are not overwritten.
- New folders follow the same rules (`Orders API` becomes `orders-api`); folders that already exist keep their spelling. A folder exists while it holds a test: one emptied by moving or deleting its last test is removed (unless other files are in it).

**Nothing is ever overwritten.** Axiom does not check-then-write: new files are created so that the file system refuses to replace an existing one, and moves are made with overwriting turned off, so even a file that appears in between is safe; the next free name is used instead. A save that also renames writes the content in place first, so a failed rename loses nothing. A change of letter case only goes through a temporary name, so it works on disks that ignore case. Renaming a folder onto one that already exists is refused rather than merging the two.

**Moving is not saving.** Moving a test or renaming its folder moves the file as it is, comments included, so Git shows a rename. Cloning copies the file as it is and changes only `name:` and `id:`.

### Test ids

Every test gets a stable `id:` when it is created (16 hex characters, e.g. `id: 3f9c2a7be41d06f5`); a test written before ids existed gets one the next time it is saved. The name is the label, the path is the location, and the id is the identity: it never changes, so a test keeps it through renames and moves (the app remembers a test's last result by it). A clone gets a new id. Two files with the same id (typically a file copied by hand) stop a run with both files named: remove the `id:` line from the copy, or use **Clone**.

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
- In the app: **Collection → Shared steps** to create groups, then **Use shared steps** in the test builder. Results show the shared steps nested under the include step.

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

A request `body` is sent as `application/json` when it is valid JSON and as `text/plain` otherwise; a `Content-Type` header on the step (or in `request_defaults`) always wins. The builder warns while you type when a body is not valid JSON (a common slip: `{title: "foo"}` instead of `{"title": "foo"}`).

Step types: `request` (`method`, `url`, `query_params`, `headers`, `body`), `db_query` (`connection`, `sql`, `save_as`) and `include` (`ref`, see Shared steps).

Variable names (collection and test `variables`, and `save_as`) may contain only letters and underscores (`base_url`, `todo_id`); `secret` is reserved. A collection that breaks this rule fails to load with the offending file and name, and the app rejects such names when saving.

Each step also stores its results in the context as `<step_id>_status`, `<step_id>_duration_ms`, `<step_id>_response_text`, `<step_id>_response_json` (request) and `<step_id>_rows`, `<step_id>_row_count` (DB), so later steps can reference them. What a step sent is stored too, before it is sent: `<step_id>_request` (`method`, `url`, `headers`, `body`, with every template resolved) and `<step_id>_sql`.

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
    provider: local            # default: value kept on this machine (entered in the app)
    key: db_password
    environments:
      ci:   { provider: env, key: DB_PASSWORD }
      prod: { provider: k8s, key: "orders-db/password" }
```

Select the environment with `axiom run <folder> --env ci` (or `AXIOM_ENVIRONMENT=ci`), or with the environment dropdown next to **Run** in the app (shown once any secret has an override). A secret with no override for the selected environment uses its default source, and environment names are case-insensitive.

All declared secrets are read before a run starts; if any cannot be read the run fails with a message naming the secret and provider (never a value).

| Provider | `key` format | Configuration (environment variables) |
| --- | --- | --- |
| `env` | `ENV_VAR_NAME` | none. Best for CI/CD |
| `file` | `relative/file/name` | `AXIOM_SECRETS_DIR`. Works with mounted Kubernetes secret volumes and Docker secrets; keys cannot escape the directory |
| `k8s` | `secret-name/data-key` or `namespace/secret-name/data-key` | In a pod it uses the service account (needs `get` on the secret). Elsewhere set `AXIOM_K8S_API_URL` (+ `AXIOM_K8S_TOKEN`); `AXIOM_K8S_NAMESPACE` sets the default namespace |
| `vault` | `mount/path#field` | `VAULT_ADDR`, `VAULT_TOKEN`, optional `VAULT_NAMESPACE`; KV v2 by default, `AXIOM_VAULT_KV_VERSION=1` for KV v1 |
| `local` | secret name | Values are supplied by the caller for a single run. The app keeps them in the OS secure storage (DPAPI on Windows, the login Keychain on macOS, libsecret's `secret-tool` on Linux) outside the collection folder, never shows them again, and refuses to store them when no secure storage is available. `axiom run` on the same machine reads them from there |

Provider connection settings come only from the environment, never from collection files, so a shared collection cannot redirect secret lookups. Any secret value of 4+ characters is replaced with `********` in reports and JSON output. To add another store, implement `ISecretProvider` and register it (see "Extending the engine").

## Assertion sources and operators

Sources:

- Request step: `status`, `duration_ms`, `body` (parsed JSON when possible, otherwise text), `body_text` (the raw response text), `headers` (response headers, looked up case-insensitively: `source: headers`, `path: content-type`)
- DB step: `row_count`, `duration_ms`, `rows`
- Any context variable name can also be referenced

In the builder, source and path are typed as one expression, e.g. `body.items.*.price` (the first segment is the source, the rest is the path); the YAML keeps them as separate `source` and `path` fields.

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

Aggregations fail with a clear message instead of guessing: a missing value or a non-list can't be aggregated, `sum` over non-numbers fails, and `avg`/`min`/`max` of an empty list fail (`sum` of an empty list is 0). Aggregation names are case-insensitive and extensible via `IAssertionAggregation`. In the builder it is the aggregation dropdown after the path field.

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

## The app

```bash
axiom ui                 # or double-click axiom.exe on Windows, or Axiom.app on macOS
```

`axiom ui` starts the local host on a free port and opens the UI in an app window of Edge or Chrome (no tabs or address bar; the default browser when neither is installed). It is the same program as the CLI: one executable, nothing else to install.

- **Double-click:** started with no arguments from Windows Explorer (`axiom.exe`) or from a macOS app bundle (`Axiom.app`: Finder, Dock, Launchpad), the program opens the app, with no console or Terminal window. Typed in a terminal without arguments it prints its help, and with arguments it is the CLI, also inside the bundle (`Axiom.app/Contents/MacOS/axiom run ...`; link it into your `PATH` to type `axiom`). On macOS the app has no Dock icon: its window is the browser window.

- **It ends with its window.** The UI pings the host while it is open and says goodbye when its window closes; the host then stops (a reload pings again, so it does not). It also stops when no UI has been heard from for 3 minutes, or when none connected within 5 minutes. Press Ctrl+C to stop it from a terminal.
- **Options:** `--no-open` prints the link instead of opening it (open it in any browser; `AXIOM_NO_OPEN=1` does the same, also for a double-click start); `--port <n>` picks the port; `--ui-dir <folder>` serves the UI from a folder instead of the built-in copy (for UI development).
- **Where it keeps things:** preferences (language, theme, recent collections) and the list of local secrets in `%APPDATA%\Axiom` on Windows, `~/Library/Application Support/Axiom` on macOS and `~/.config/Axiom` on Linux (`AXIOM_DATA_DIR` overrides it); local secret values in the OS secure storage (see [Secrets](#secrets)).
- **macOS keychain permission:** the keychain remembers which program stored a secret. A rebuilt or newly downloaded `axiom` is a new program to it, so the first time it reads a stored secret, macOS asks; choose **Always Allow**. The app and a CLI run in Terminal can show that question; a CLI run with no terminal (a script) cannot, so instead of waiting it reports that the secret needs permission. Releases signed with the same Developer ID keep access across updates. A run only reads the local secrets it needs: with an environment whose secrets come from elsewhere (`--env ci`), the keychain is not touched at all.
- **Proxies:** the UI only talks to the local host on `127.0.0.1`, which browsers never send through a proxy; all outgoing traffic (your APIs, OpenAPI documents) is made by the engine.

Moving from the Electron app: local secret values it stored cannot be read by the new app (they were encrypted by Electron); enter them once more under **Collection → Secrets**.

### Building

```bash
cd ui && npm ci && npm run build && cd ..      # the web UI, into ui/dist
dotnet run --project backend/src/Axiom -- ui    # the app, from source
```

The build embeds `ui/dist` into the executable (a build without it warns, and `axiom ui` then has no UI); `-p:BuildUi=true` runs the npm steps as part of the .NET build.

**While working on the UI:** `npm run watch` in `ui/` rebuilds on every change, and `dotnet run --project backend/src/Axiom -- ui --ui-dir ui/dist` serves that folder, so reloading the page shows the change without rebuilding the executable. `npm run format` runs Prettier.

### Publishing a single executable

```bash
dotnet publish backend/src/Axiom -c Release -r win-x64   -p:BuildUi=true -o publish/win-x64     # axiom.exe
dotnet publish backend/src/Axiom -c Release -r osx-arm64 -p:BuildUi=true -o publish/osx-arm64   # axiom
dotnet publish backend/src/Axiom -c Release -r linux-x64 -p:BuildUi=true -o publish/linux-x64   # axiom
```

Each is one self-contained file with the .NET runtime, the UI and the SQLite / SQL Server native libraries inside: about 55 MB on Windows and Linux (compressed) and 120 MB on macOS (not compressed: a compressed single file crashes there on the first HTTPS request after a keychain read).

Publishing for macOS (`osx-arm64`, `osx-x64`) also makes **`Axiom.app`** next to the executable, with its icon and `Info.plist`, ad-hoc signed so it runs on Apple Silicon, and, when publishing on a Mac, **`Axiom-osx-arm64.zip`** of it (about 45 MB) to hand out. Set `-p:AxiomVersion=1.2.0` for the version the Finder shows and `-p:AxiomBundleIdentifier=com.yourcompany.axiom` for your own bundle id.

Sign them for distribution (Authenticode on Windows; a Developer ID signature and notarization of `Axiom.app` on macOS) so SmartScreen and Gatekeeper let them run on other machines: an unsigned download is blocked, and a Developer ID also keeps keychain access across updates.

## Next build targets

1. JSON path / response schema assertion builder in UI
2. Rich report screen with trends and failed-step diagnostics
3. Plugin-based connectors (load extra providers, step types and operators from a plugins folder)
4. Code signing and notarized downloads for the published executables
