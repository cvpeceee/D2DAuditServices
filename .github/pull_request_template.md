## What changed

-

## Checklist

- [ ] Domain stays free of infrastructure and host dependencies. New boundaries are interfaces the application owns.
- [ ] Expected failures use `Result<T>` or FluentValidation. Exceptions are reserved for bugs and startup misconfiguration.
- [ ] New I/O is async and takes a `CancellationToken`.
- [ ] Logs have no passwords, tokens, or email addresses.
- [ ] `dotnet test src/api/D2DAudit.slnx` passes, including the architecture tests.
- [ ] Web changes keep strings in the translation files and pass `npm run lint`, `npm run format:check`, `npm test`, and `npm run build`.
- [ ] If the HTTP contract changed, `src/web/openapi/v1.json` and `schema.ts` were regenerated.
- [ ] No secrets in the diff. Example passwords stay the documented development values.
