# Engineering standards

Short rules for this repo. Prefer a straight C# or TypeScript reading over a new abstraction.

## Shape

- Domain has no project dependencies. Application defines the use-case interfaces and `Result<T>`. Infrastructure implements them. The API project is the composition root: thin controllers, authentication, and the HTTP mapping.
- Dependency injection is the only way to get a service. No service locator and no mutable static fields. Pure helpers and `const` / `static readonly` values are fine.
- Put an interface on a boundary (identity, tenancy, persistence, jobs). Do not add an interface for every class.
- Architecture tests in `D2DAudit.Tests` lock the layering and the static-field rule.

## C#

- Nullable reference types are on. Warnings are errors. `.editorconfig` plus the .NET analyzers and SonarAnalyzer run in CI.
- StyleCop is not enabled. Its file-header and usings-inside-namespace rules fight the SDK style and do not make the code easier to read.
- Async methods take a `CancellationToken` and pass it through.
- Expected failures return `Result<T>` or a validator error (FluentValidation). Throw for bugs and for startup misconfiguration, such as a half-set WorkOS configuration or an unsafe database name.
- Logging is `Microsoft.Extensions.Logging`. Do not log passwords, tokens, or email addresses. The activity table stores the actor email because that is the product audit record, not an application log.
- OpenTelemetry tracing and metrics are registered at startup. The OTLP exporter is added only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- Unit tests (`D2DAudit.UnitTests`) cover domain and application code and do not need a database. Integration tests use Testcontainers Postgres. Set `TEST_POSTGRES` to an existing admin connection when Docker is not available. The role needs `CREATEDB`.

## Web

- TypeScript is strict. ESLint (including `jsx-a11y`) and Prettier run in CI.
- UI code lives under `src/web/src/features`. Shared API, theme, i18n, and small UI helpers live under `src/shared`.
- `src/web/src/shared/api/schema.ts` is generated from `src/web/openapi/v1.json`. Regenerate with `npm run openapi:types` after the HTTP contract changes. The fetch wrapper in `client.ts` stays handwritten so it can send the session cookie, the CSRF header, and `X-Tenant-Id`.
- Vitest and Testing Library cover feature and UI behavior.

## Cookies

Session cookies are `Secure` outside Development and Testing. In Development, `Secure` follows the request scheme so `http://localhost` still works. Production hosts always set `Secure`.
