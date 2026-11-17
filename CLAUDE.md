# Pracownia Bajkowe Cuda — guide for Claude Code

Mobile-first one-page website for a custom toy-sewing **service** (not a shop). Customers sign in, talk to the owner
in a chat, and follow their orders. The owner (Admin) creates and manages the orders. The project is also a portfolio
piece for **Docker, Azure and CI/CD**, so infrastructure and pipeline quality matter as much as features.

**The full specification and roadmap is `docs/PLAN.md`. Read it before starting any work.**
Record the decisions you make along the way in `docs/DECISIONS.md` (short ADR-style entries).

## Stack (summary)
- Backend: ASP.NET Core (.NET 10, latest LTS), Clean Architecture, EF Core + SQL Server / Azure SQL, ASP.NET Core Identity, SignalR.
- Frontend: Angular (latest stable), standalone components, signals, `@angular/localize` (en, de, fr, es, pl), static prerendering of public pages.
- One Docker image: ASP.NET Core serves the API, the SignalR hub and the prerendered Angular files (same origin, BFF cookie auth).
- Azure: Container Apps, Azure SQL, Blob Storage, Key Vault, Communication Services Email, Application Insights. Infrastructure as code in Bicep.
- CI/CD: GitHub Actions (PR checks → staging automatically → production after manual approval), Azure login through OIDC.

## How to work
1. Work **one phase at a time**, in the order given in `docs/PLAN.md` §15. Do not start the next phase until the current
   phase's acceptance criteria pass. Tick the phase checklist items in `docs/PLAN.md` as they are completed.
2. If the plan is ambiguous, or you want to deviate from it, stop and ask the user. Do not silently change scope.
3. Keep `README.md` up to date: what the project is, how to run it locally, architecture diagram, CI/CD badges.
4. Before reporting a task as done: build passes, all tests pass, lint passes, `docker compose up` still works.

## Commands (keep this section current once the projects exist)
```bash
docker compose up -d                       # SQL Server, Azurite, Mailpit
docker compose --profile app up -d --build # + the application image on http://localhost:8080
scripts/smoke-test.sh http://localhost:8080 # checks a running instance (redirect, pages per locale, headers)
dotnet build BajkoweCuda.sln
dotnet test --solution BajkoweCuda.sln     # Microsoft.Testing.Platform runner (see global.json)
dotnet run --project src/backend/BajkoweCuda.Api   # API on http://localhost:5080
cd src/frontend && npm ci && npm start     # ng serve on :4200 with proxy (/api, /hubs, /health) to the API
cd src/frontend && npm test && npm run lint && npm run format:check
cd src/frontend && npm run audit           # npm audit; accepted advisories with a review date: audit-allowlist.json
cd src/frontend && npm run build           # all 5 locales, prerendered, + robots.txt / sitemap.xml
cd src/frontend && npm run i18n:extract    # after changing UI text: re-extract and merge src/locale/*.xlf
npx @lhci/cli@0.16.1 autorun               # Lighthouse CI against http://localhost:8080 (.lighthouserc.json)
dotnet ef migrations add <Name> -p src/backend/BajkoweCuda.Infrastructure -s src/backend/BajkoweCuda.Api
az bicep build --file infra/main.bicep --stdout   # compile-check the infrastructure (also infra/shared.bicep)
```
Deployment is done by `.github/workflows/cd.yml` only (never by hand); operations are in `docs/RUNBOOK.md`.

On the owner's Windows machine Smart App Control blocks freshly built .NET binaries, so `dotnet test` and `dotnet run`
do not work there. Build with `dotnet build`, run the backend tests in a container, and run the application itself with
`docker compose --profile app up -d --build`:
```bash
docker run --rm -v "${PWD}:/src:ro" -w /src -v bajkowecuda-nuget:/root/.nuget/packages mcr.microsoft.com/dotnet/sdk:10.0 dotnet test --solution BajkoweCuda.sln --artifacts-path /tmp/artifacts
```

## Rules
- **Secrets never go into the repo.** Locally: `dotnet user-secrets` / `.env` (git-ignored, with `.env.example` committed). Azure: Key Vault + Managed Identity.
- Conventional Commits (`feat:`, `fix:`, `chore:`, `ci:`, `infra:`, `docs:`, `test:`). Small, focused commits.
- C#: nullable enabled, warnings as errors, analyzers on, central package management (`Directory.Packages.props`).
  No MediatR and no AutoMapper (both are commercially licensed now): use simple handler classes and explicit mapping.
- Dependency direction: Domain ← Application ← Infrastructure / Api. Domain has no framework dependencies.
- Every endpoint is authorized explicitly (`[Authorize]` by default, `[AllowAnonymous]` only where intended).
  Customer endpoints always filter by the current user's id. Admin endpoints use the `Admin` policy.
- Never log message bodies, email addresses in full, tokens or file contents.
- Public (prerendered) pages must not call the database. Only signed-in features may.
- User-facing text must be translatable (Angular `i18n` / `$localize`; backend `.resx`). The source language is English.
- Write tests together with the code: domain logic → unit tests; endpoints → integration tests (including authorization/IDOR cases).
