# Axiom — Technical Report

_Date: 2026-09-26 · Scope: `main` @ `e1108bf` (backend engine, CLI, local host, Electron/React desktop)_

## 1. Summary

Axiom is a no-code API testing tool. You write test scenarios as YAML, either by hand or in a visual builder. A .NET 10 engine runs them from a CLI in CI, and the same engine runs locally behind an Electron desktop app.

The **engine is the strongest part of the project**. It has a clean core/host split, explicit extension points, careful concurrency, a well-designed assertion model (Passed / Failed / Error), and solid secret handling. It is backed by 245 fast, offline unit tests; all passed on this review (≈2 s).

The **main gaps are around the engine**:

- The CLI is not yet CI-grade: it has no test filtering, no JUnit/TRX output, and no packaged binary.
- The desktop app has no quick "send one request" loop, and it runs the whole collection with no progress or cancel.
- The UI rewrites the YAML, which removes comments and drops unknown fields. That works against the "YAML lives in Git" promise.
- The "extensible" story stops at the backend. The step model and the UI's operator and step lists are hardcoded.

| Area | Rating | One-line verdict |
| --- | --- | --- |
| Core engine design | ●●●●○ | Clean, DI-driven, well-tested; step model is the weak spot |
| Assertion model | ●●●●● | Rich operators, aggregations, wildcard paths, clear failure vs error |
| Performance | ●●●○○ | Good ideas (lazy JSON, bounded channel); DB serialization and full buffering limit scale |
| CLI / CI usage | ●●○○○ | Correct exit codes and JSON envelope, but missing filtering, reports, packaging |
| Desktop UX | ●●●○○ | Polished basics (i18n, themes, dirty tracking); slow feedback loop for authoring |
| Security | ●●●○○ | Strong secret handling; unauthenticated localhost API on a fixed port |
| Extendability | ●●●○○ | Backend plug points exist; UI and step schema don't follow |
| Test coverage | ●●●●○ | Backend thorough; frontend and Electron have none |
| Delivery / DevOps | ●○○○○ | No CI pipeline, no packaging, desktop requires .NET SDK |

---

## 2. Architecture overview

```
┌──────────── Electron main (main.js) ─────────────┐
│ IPC handlers ── fetch ──► http://127.0.0.1:50743 │   spawns `dotnet run --no-build -- serve`
│ safeStorage (local secrets)                       │
└───────────────▲──────────────────────────────────┘
                │ contextBridge (preload.js, 21 methods)
┌───────────────┴──────────┐        ┌────────────── Axiom (exe) ───────────────┐
│ React renderer (Vite)    │        │ ProgramEntry: `run` | `serve`            │
│ App.jsx + 9 components   │        │ HostServerService: minimal API routes    │
└──────────────────────────┘        │ ReportFormatterService: text report      │
                                    └──────────────┬───────────────────────────┘
                                                   │ AddAxiomCore()
                                    ┌──────────────▼──── Axiom.Core ───────────┐
                                    │ Parsing → CollectionRunner → TestCase-   │
                                    │ Executor → StepRunner → IStepExecutor[]  │
                                    │ AssertionEngine(IAssertionOperator[],    │
                                    │   IAssertionAggregation[])               │
                                    │ SecretResolver(ISecretProvider[])        │
                                    │ DbQueryExecutor(IDbConnectionFactory[])  │
                                    │ CollectionManagementService, OpenApi…    │
                                    └──────────────────────────────────────────┘
```

Size: about 4.5k lines of C# (engine and host), about 3.5k of JS/JSX, about 1.9k of CSS, and about 2.6k lines of tests.

---

## 3. Software design

### Strengths

- **Clear layering.** `Axiom.Core` has no ASP.NET dependency, so the same engine serves the CLI, the host, and could serve a future NuGet package or test adapter. The executable is a thin shell ([ProgramEntry.cs](../backend/src/Axiom/ProgramEntry.cs), [HostServerService.cs](../backend/src/Axiom/Hosting/HostServerService.cs)).
- **Strategy + DI everywhere it matters.** Step executors, step validators, assertion operators, aggregations, DB providers and secret providers are all resolved from `IEnumerable<T>` and looked up by name ([AxiomServiceCollectionExtensions.cs](../backend/src/Axiom.Core/AxiomServiceCollectionExtensions.cs)). The README documents these extension points.
- **Scoped per-run lifetime.** One DI scope per run owns the `HttpClient`, the cached DB connections and the local secrets, and disposes them at the end ([CollectionRunner.cs:33](../backend/src/Axiom.Core/Runtime/CollectionRunner.cs#L33)).
- **Honest result semantics.** `RunOutcome` separates *Failed* (the API misbehaved) from *Error* (the test or environment is broken) at the assertion, step and test levels. This distinction is rare in no-code tools and very useful for triage ([AssertionEngine.cs:87-129](../backend/src/Axiom.Core/Runtime/AssertionEngine.cs#L87-L129)).
- **Fail fast before side effects.** All secrets are resolved before any step runs. Include references and variable names are validated at load time.
- **Defensive file handling.** File names cannot escape their folder ([CollectionPaths.cs:64](../backend/src/Axiom.Core/Parsing/CollectionPaths.cs#L64)). Slugs are deterministic and never collide. An OpenAPI re-import never overwrites edited tests.
- **Readable code.** Naming is consistent, intent is explained in comments, and the public API has XML docs. Classes are small; the largest engine class is about 400 lines.

### Weaknesses

- **The step model is a flat union.** [StepDefinition.cs](../backend/src/Axiom.Core/Models/StepDefinition.cs) carries `Method/Url/Headers/Body` (request), `Connection/Sql` (db) and `Ref` (include) on one class. A plugin step type (gRPC, Kafka, "wait until") cannot add its own fields without editing core. This undermines the `IStepExecutor` extension point. Options: a `Dictionary<string, object?> Extra` / `[YamlExtensionData]` bag, or polymorphic deserialization keyed on `type`.
- **Two parallel model hierarchies.** `Models/*Definition` (runtime) and `Documents/*Document` (editing) mirror each other. Every new field has to be added twice and normalized twice ([YamlCollectionLoader.cs](../backend/src/Axiom.Core/Parsing/YamlCollectionLoader.cs) vs [CollectionManagementService.cs](../backend/src/Axiom.Core/Services/CollectionManagementService.cs)).
- **Unknown YAML keys are silently ignored.** `IgnoreUnmatchedProperties()` in [YamlSerialization.cs](../backend/src/Axiom.Core/Serialization/YamlSerialization.cs) means a typo such as `asert:` or `qurey_params:` gives a test that passes vacuously. For a tool whose users edit YAML by hand, this is the most dangerous silent failure in the codebase.
- **Saving from the UI is lossy.** `SerializeFile` re-serializes the whole document. YAML comments, key order, anchors and any field unknown to the `Document` DTOs are lost on every save from the desktop app. This conflicts with the Git-first positioning and will produce noisy diffs.
- **`TemplateResolver` is static and closed.** There is no way to add functions (`{{$uuid}}`, `{{$now}}`, `{{$randomInt}}`, base64, hashing), which API testers commonly need. It also falls back to reflection (`GetProperty`) for unknown object types.
- **Culture-sensitive formatting.** Templates render values with `object.ToString()` ([TemplateResolver.cs:31](../backend/src/Axiom.Core/Runtime/TemplateResolver.cs#L31)), and the process does not force `InvariantCulture`. On a `tr-TR` machine, where the UI already supports Turkish, a double such as `duration_ms` or a numeric variable renders as `1,5`.
- **Mutable shared context.** Steps write `<id>_status`, `<id>_response_text` and similar entries into one `Dictionary<string, object?>`. This works, but the variable namespace can collide (`save_as: login_status` vs step `login`), and the only schema of what a step exposes is in the README.
- **Minor issues:**
  - Duplicate `<summary>` blocks on primary constructors (e.g. [CollectionRunner.cs:10-15](../backend/src/Axiom.Core/Runtime/CollectionRunner.cs#L10-L15)).
  - Static mutable `isJsonResponseMode` in `ProgramEntry`.
  - `AddScoped<HttpClient>` registers a bare `HttpClient` in the host's container, which leaks into any consumer that calls `AddAxiomCore()`. Prefer a typed or named client.

---

## 4. Performance

### Strengths

- **Bounded channel with N workers** gives parallel test execution with backpressure ([CollectionRunner.cs:42-70](../backend/src/Axiom.Core/Runtime/CollectionRunner.cs#L42-L70)).
- **Lazy JSON parsing.** A body is parsed only when something reads it. `contains` on a whole body searches raw text, and parallel tests get cloned `LazyJson` so they never race ([LazyJson.cs](../backend/src/Axiom.Core/Runtime/LazyJson.cs)).
- **Run-once shared steps** use `Lazy<Task<…>>`, so an auth login runs once even with many parallel tests ([SharedStepsLibrary.cs](../backend/src/Axiom.Core/Runtime/SharedStepsLibrary.cs)).
- **Regex safety and speed.** Compiled patterns are cached and have a 1 s match timeout, which prevents ReDoS ([ValueComparison.cs:19](../backend/src/Axiom.Core/Runtime/ValueComparison.cs#L19)).
- A per-step `CancellationTokenSource` enforces `step_timeout_seconds`.

### Weaknesses

- **DB steps are serialized across parallel tests.** Each distinct connection string maps to one cached connection guarded by a `SemaphoreSlim(1,1)` ([DbQueryExecutor.cs:31](../backend/src/Axiom.Core/Runtime/DbQueryExecutor.cs#L31)). With `max_parallel_test_cases: 8` and a DB setup step in every test, the DB part runs one query at a time. SQL Server has built-in pooling, so opening a connection per query (or a small pool) would scale better.
- **Everything is buffered.** Response bodies are read fully with `ReadAsStringAsync`, and DB results are read fully into `List<Dictionary<…>>` with no row cap. A large response or `SELECT *` on a big table can exhaust memory. Add a max body size and a max row count.
- **No streaming of results.** The CLI prints nothing until the whole run ends. The desktop app blocks on a single `POST /api/run`, so there is no progress, no partial results and no cancel. For a 10-minute suite this looks like a hang.
- **HTTP client configuration is fixed.** It is a plain `new HttpClient()`: default 100 s timeout, no configurable proxy, no TLS options (self-signed certs in test environments), no client certificates, no redirect policy, no HTTP/2 toggle.
- **Desktop cold start.** `dotnet run --no-build` goes through the SDK, adding a second or more on every launch. If the backend was never built, it fails with a stderr dump, because `npm start` does not build the backend. The 15 s startup timeout can trigger on slow machines.
- Masking walks every secret over every result string. That is fine at today's scale, but it is O(secrets × text).

---

## 5. CLI usage

### Strengths

- Simple surface: `run <folder> [--env] [--json]` and `serve [--port]`.
- **Meaningful exit codes**: `0` pass, `2` test failures/errors, `3` runtime failure, `1` usage error.
- A machine-readable **JSON envelope** (`{ ok, data, error }`) with stable error codes.
- Environment selection by flag or `AXIOM_ENVIRONMENT`, which suits CI.

### Weaknesses

| Gap | Impact |
| --- | --- |
| No `--filter` / `--tag` / single-file run | Can't run a subset in CI or locally; tests have no tags field |
| No JUnit / TRX / HTML report | CI systems (GitHub Actions, Azure DevOps, GitLab, Jenkins) can't show per-test results natively |
| No `validate`, `init`, `import-openapi` commands | These exist only behind the HTTP host; you can't lint a collection in a PR pipeline |
| No `--var key=value` overrides | You can't point the same collection at a different `base_url` without editing YAML or adding secrets |
| No packaged binary / `dotnet tool` | Users must clone the repo and `dotnet run --project …`; there is no `axiom` command |
| Silent option handling | `--env` with no value, or unknown flags such as `--prallel`, are ignored without a warning |
| No Ctrl+C handling | `serve` passes `CancellationToken.None`, and `run` does not hook `Console.CancelKeyPress`, so in-flight requests aren't cancelled cleanly |
| Report verbosity | Every passing assertion is printed, with absolute paths and ISO timestamps. There is no colour, `--quiet`, or failures-only mode |
| No `--version`, `--help` per command, `--fail-fast`, `--parallel N` override | Standard CLI conventions are missing |

---

## 6. UI / UX and usage flow

### Current flow

1. **Welcome**: open a folder, create an empty collection, or import from an OpenAPI URL. Recent collections are listed, and the last one reopens automatically.
2. **Collection**: tests, shared steps, variables, connections, secrets, and an environment dropdown.
3. **Builder**: ordered, collapsible steps (request / SQL / include); key-value editors; an assertion grid (expression + aggregation + operator + expected + options); variable autocomplete; a YAML preview; dirty tracking.
4. **Run → Results**: a pass/fail/error summary, a filter, per-test expansion with nested shared steps, expected vs actual values, and the raw text report.

### Strengths

- **Built for its target user.** A source and path are typed as one expression (`body.items.*.price`), operators are grouped with human-readable labels, and the expected field adapts to the operator (type dropdown for `is_type`, disabled for `exists`).
- **Good result diagnostics.** Failures show expected vs actual. "Could not evaluate" errors are shown in amber, apart from red failures. Failed tests are expanded by default.
- **Safety details.** Unsaved-changes guard, delete confirmation, and local secrets stored with the OS keychain that the renderer can write but never read back ([main.js:208-224](../desktop/main.js#L208-L224)).
- **Polish.** EN/TR i18n, light/dark/system theme, toast notifications, empty states, and some ARIA labels on icon buttons and toggles.
- **Secure Electron defaults.** `contextIsolation: true`, `nodeIntegration: false`, and a narrow preload API.

### Weaknesses

- **Slow authoring feedback loop.** You cannot send a single request and look at the response while building a test. To see what `body.data.0.id` looks like, you must save and run the whole collection. For a no-code tool, the ability to send a request, click a field and generate an assertion (as in Postman or Insomnia) is the most valuable missing feature.
- **No run scoping.** There is no "run this test", "run selected" or "re-run failed". The Run button always runs everything.
- **No progress or cancel during a run.** Only a spinner is shown (see Performance).
- **Results are not persisted.** Closing the app or switching collections loses the last report. There is no run history (it is listed as a "next build target").
- **Native `window.confirm` / `window.alert` dialogs** ([App.jsx](../desktop/renderer/src/App.jsx)) clash with the custom design and can't be translated or styled.
- **The YAML preview is hand-built in JS** ([i18n.js:356](../desktop/renderer/src/i18n.js#L356)), not produced by the real serializer. It can drift from what is saved; for example, names containing `'` are quoted incorrectly.
- **Local secrets are keyed by absolute folder path.** Moving or renaming the collection folder silently loses them.
- **Fixed minimum window size of 980×700.** There is no responsive layout for smaller or split screens.
- **Accessibility is partial.** Icon buttons have labels, but keyboard reordering of steps, focus management after add/delete, and live regions for run results are missing.

---

## 7. Frontend code quality

### Weaknesses

- **`App.jsx` is a god component.** It has 732 lines and about 30 `useState` hooks, and it holds all I/O handlers and prop-drills `t` everywhere. There is no reducer, context, router or data layer. Adding features will get harder quickly.
- **Misleading module boundaries.**
  - [i18n.js](../desktop/renderer/src/i18n.js) holds translations *and* domain logic (`buildReport`, `normalizeSteps`, `toYamlSteps`, `yamlPreview`).
  - Translations are split between `i18n.js` and [strings.js](../desktop/renderer/src/strings.js), described as "added by the redesign", which means two places to look for any string.
- **Hardcoded domain knowledge.** Operators, step types and sources are hardcoded in [Builder.jsx:7-24](../desktop/renderer/src/components/Builder.jsx#L7-L24). Aggregations are fetched from the backend, but operators and step types are not. A custom `IAssertionOperator` or `IStepExecutor` registered in the backend never appears in the builder.
- **No TypeScript, ESLint, or frontend tests.** Only Prettier is set up. The IPC contract between `preload.js`, `main.js` and the host routes is untyped and repeated in three places.
- **Dirty checking via `JSON.stringify`** on every render. This is fine now, but fragile, because it depends on key order.
- **Styling.** Bootstrap 5 plus about 1,900 lines of custom CSS; Bootstrap is used mainly for form classes, so it could likely be dropped.

---

## 8. Security

### Strengths

- Secret provider connection settings come **only from the environment**, so a malicious shared collection cannot redirect Vault or Kubernetes lookups.
- Secret values of 4 or more characters are masked in every result, error and report.
- The file provider prevents directory escape, and test and shared file names are validated against path traversal.
- Local secrets are encrypted with `safeStorage`, and the app refuses to fall back to the `basic_text` backend.

### Weaknesses

- **The localhost API is unauthenticated and uses a fixed port (50743).**
  - Any local process can read, write and delete YAML under *any* `folderPath`, or trigger runs.
  - `POST /api/run` with an empty body is a CORS "simple request", so a web page open in the user's browser can trigger a run of a known folder path (CSRF).
  - A fixed port also means a second app instance, or another tool on that port, breaks startup.
  - **Fix:** bind to port 0, pass a random bearer token from Electron to the host through an environment variable, and require it on every route.
- **SQL templating is string interpolation.** `{{var}}` values, including values taken from API responses, are pasted into SQL. That is acceptable for a test tool, but add a parameterized form (`params:`) and document the risk.
- **Error text leaks.** Host errors return `ex.Message` directly. This is fine locally but should be reviewed if the host is ever exposed.

---

## 9. Extendability

| Extension | Backend | Desktop UI | Verdict |
| --- | --- | --- | --- |
| Assertion operator | ✅ `IAssertionOperator` | ❌ hardcoded list | Half-done |
| Aggregation | ✅ `IAssertionAggregation` | ✅ fetched from host | Complete |
| Step type | ⚠️ `IStepExecutor`, but no custom fields | ❌ request/SQL/include hardcoded | Weak |
| DB provider | ✅ `IDbConnectionFactory` | ⚠️ free text | Mostly fine |
| Secret provider | ✅ `ISecretProvider` | ✅ fetched from host | Complete |
| Template functions | ❌ static resolver | ❌ | Missing |
| Report formats | ❌ single text formatter, `internal` | n/a | Missing |
| Plugin loading | ❌ must recompile | ❌ | On roadmap |

To make extensibility real, have the host expose a **schema endpoint** describing step types, their fields, operators (with arity and expected-value kind) and aggregations. The builder should render from that schema. This is also the natural first step toward the planned plugins folder.

---

## 10. Testing and delivery

### Strengths

- 245 xUnit tests that pass in about 2 s, with no network. They include a fake Kubernetes/Vault server and a stubbed HTTP handler, and they cover whole-collection runs through the real DI container.
- Concurrency-sensitive behaviour (run-once shared steps under parallel tests, cycle detection) is tested explicitly.

### Weaknesses

- **No CI pipeline.** There is no `.github/workflows`, so the tests only run when someone remembers to run them.
- **No tests for the desktop app** (renderer or Electron main), and no end-to-end test of the IPC → host path.
- **No packaging.** There are no installers, no self-contained or single-file publish, and no `dotnet tool`. The desktop app requires the .NET SDK and a manual backend build.
- **No sample collection in the repo.** `samples/` is git-ignored, so new contributors have nothing to run.
- **No versioned YAML schema.** The README says "v0", but no `version:` field or JSON Schema exists. Such a schema would also enable editor autocompletion (for example through the VS Code YAML extension).

---

## 11. Prioritized recommendations

**P0: correctness and trust**

1. Reject unknown YAML keys, or at least warn about them, at load time. Typos must not produce passing tests.
2. Stop lossy saves: preserve unknown fields (extension data) and ideally comments (round-trip through the YAML representation model instead of POCOs).
3. Secure the local host: random port plus a per-launch bearer token.
4. Format template values with `CultureInfo.InvariantCulture`.

**P1: CI readiness**

5. Add `--filter`, a `tags:` field, and `--reporter junit|json|text` (JUnit XML first).
6. Add `validate` and `import-openapi` CLI commands, and `--var key=value`.
7. Ship `axiom` as a `dotnet tool` and as self-contained single-file binaries. Add a GitHub Actions workflow for build, test and publish.
8. Handle Ctrl+C with cancellation and print partial results.

**P2: authoring experience**

9. Add single-request "Send" in the builder, with a response viewer and "click to assert".
10. Add run-selected and re-run-failed, streaming progress (SSE or chunked from the host), and a cancel button.
11. Persist run history per collection.
12. Have the host serve a builder schema so custom operators and step types show up in the UI automatically.

**P3: scale and maintainability**

13. Replace the DB single-connection gate with per-query connections or a pool, and add body-size and row-count caps.
14. Add configurable HTTP options: timeout, proxy, TLS verification toggle, client certificate.
15. Split `App.jsx` into a store (reducer or context) plus feature hooks, move domain logic out of `i18n.js`, merge the translation files, and add TypeScript and ESLint.
16. Replace native dialogs with in-app modals.
17. Add extensible template functions and a pluggable report formatter.

---

## 12. Strengths and weaknesses at a glance

**Strengths**

- Clean engine/host separation with DI-based extension points.
- A well-designed assertion language: operators, aggregations, wildcards, strict/case/tolerance options, and Failed vs Error.
- Careful concurrency: a bounded channel, run-once shared steps, and per-test JSON clones.
- Performance-aware details: lazy JSON parsing, raw-text search, regex caching with timeouts.
- Strong secret model: environment-only provider config, masking, OS keychain, per-environment sources.
- Safe file handling and stable, readable file names; OpenAPI import doesn't clobber edits.
- A fast, offline, broad backend test suite; readable, documented code.
- A friendly desktop app for non-programmers: i18n, themes, dirty tracking, clear result diagnostics.

**Weaknesses**

- Silent acceptance of unknown YAML keys, and lossy UI saves (comments and unknown fields dropped).
- A CLI that is not yet CI-grade: no filtering, no JUnit output, no packaging, no Ctrl+C handling.
- No single-request authoring loop, no progress or cancel, no run history in the desktop app.
- A flat step model and hardcoded UI lists undercut the extensibility story.
- DB access serialized per connection; responses and result sets fully buffered.
- An unauthenticated localhost API on a fixed port.
- A monolithic `App.jsx`, domain logic in the i18n module, and no TypeScript, linting or frontend tests.
- No CI pipeline, installers or checked-in samples.
