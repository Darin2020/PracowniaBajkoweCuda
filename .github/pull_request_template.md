## What and why

<!-- One or two sentences. Link the phase / checklist item in docs/PLAN.md §15. -->

## Checklist

- [ ] Build passes with no warnings (`dotnet build BajkoweCuda.sln`)
- [ ] Tests added or updated and passing (`dotnet test --solution BajkoweCuda.sln`, `npm test`)
- [ ] Lint and format pass (`npm run lint`, `npm run format:check`)
- [ ] `docker compose up -d` still works
- [ ] New endpoints are authorized explicitly; customer endpoints filter by the current user's id
- [ ] No secrets, tokens, message bodies or full email addresses in code or logs
- [ ] User-facing text is translatable
- [ ] `README.md`, `docs/PLAN.md` checklist and `docs/DECISIONS.md` updated where relevant
