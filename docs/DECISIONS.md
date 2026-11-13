# Architecture Decision Log

Short entries: date, decision, reason, consequences. Newest at the bottom. Claude Code appends new entries here.

## 2026-11-12 — Initial decisions (agreed with the owner)
See `docs/PLAN.md` §2 for the full table. Summary:
- Angular frontend with static prerendering per locale. ASP.NET Core backend in Clean Architecture. One container serving both.
- BFF cookie authentication (no tokens in the browser). Google + Facebook first, Apple when a domain exists. No Instagram (Meta removed consumer Instagram login in Dec 2024).
- Azure SQL for relational data, Blob Storage for images (private, served through an authorized endpoint). Gallery images are static assets.
- Orders are created only by an admin from the chat. No online payments. Payment instructions are a config template.
- Email: admin always notified, customer optionally (checkbox in the chat, asked on the first message), debounced digests through an outbox.
- Languages en/de/fr/es/pl, currency EUR, content edited in code (no CMS).
- Only strictly necessary cookies, so no consent banner is needed. Legal texts to be reviewed by a lawyer.
- Azure Container Apps + Bicep + GitHub Actions with OIDC. Staging (scale to zero, free SQL) and production (min 1 replica).

## 2026-11-13 — Phase 0: repository foundation

- **SDK pinning.** `global.json` pins the .NET 10 SDK (`10.0.100`, `rollForward: latestFeature`, no prereleases); every project
  targets `net10.0`. Reason: the plan fixes the target on .NET 10 LTS, and a newer or prerelease SDK installed side by side must not
  silently change analyzers or build behaviour. CI installs `10.0.x`, so local and pipeline builds use the same SDK line.
- **Classic `.sln`, not `.slnx`.** The plan and the documented commands name `BajkoweCuda.sln`; kept for the widest tooling support.
- **Tests: xUnit v3 on Microsoft.Testing.Platform.** xUnit v3 4.x no longer supports the VSTest target on the .NET 10+ SDK, so
  `global.json` opts in to the MTP runner. Consequences: the command is `dotnet test --solution BajkoweCuda.sln` (not
  `dotnet test BajkoweCuda.sln`); coverage and TRX come from `Microsoft.Testing.Extensions.CodeCoverage` / `.TrxReport`
  instead of coverlet. Assertions: Shouldly. Mocks: NSubstitute (as suggested in the plan).
- **Architecture tests from day one.** The first unit tests assert the dependency direction (Domain references only the BCL;
  Application references no outer layer, ASP.NET Core, EF Core or Azure SDK), so the rule in `CLAUDE.md` is enforced by CI.
- **Liveness endpoint runs no checks.** `/health/live` uses `Predicate = _ => false`, so health checks registered later
  (database, blob) can never leak into the probe and wake the serverless database. They belong to `/health/ready` (added with those dependencies).
- **Frontend.** Angular 22 (zoneless by default), Vitest (the default runner of this version), ESLint via `angular-eslint`,
  Prettier with `eslint-config-prettier`. Client-rendered for now; `@angular/ssr` prerendering and `@angular/localize` arrive in Phase 1.
- **Dev proxy also forwards `/health`** (besides `/api` and `/hubs` from the plan), so the proxy can be verified before any `/api` endpoint exists.
- **Compose.** Ports are bound to `127.0.0.1` only. The SQL Server `sa` password comes from `.env` and has no default, so no
  password is committed. The `app` profile is added in Phase 1 together with the `Dockerfile`.
- **CI.** Actions are pinned to commit SHAs with the version in a comment (Dependabot keeps them current). `ci.yml` also validates
  the compose file. CodeQL additionally scans the workflow files (`actions` language).
- **LICENSE** copyright holder is "Pracownia Bajkowe Cuda" (change it if a personal name is preferred).
