# Runbook

How to set up, deploy, roll back and look after Pracownia Bajkowe Cuda in Azure.
Infrastructure is described in `infra/` (Bicep); the pipeline is `.github/workflows/cd.yml`.

## Overview

| | Staging | Production |
|---|---|---|
| Resource group | `rg-bajkowecuda-staging` | `rg-bajkowecuda-prod` |
| Container App | `ca-bajkowecuda-staging` | `ca-bajkowecuda-prod` |
| Replicas | 0–1 (scales to zero when idle) | 1 (always on) |
| Search engines | blocked (`X-Robots-Tag`, `robots.txt`) | allowed |
| Deployed | automatically after CI passes on `main` | after approval in the `production` GitHub environment |

The container registry and the Container Apps environment `cae-bajkowecuda-shared` are shared
(`rg-bajkowecuda-shared`): the subscription allows one Container Apps environment per region, so both apps run in it.
Everything else (identity, database, storage, Key Vault, email, Application Insights, budget) is per environment.
The image is **built once**; the same digest goes to
staging and then to production. The public origin is configuration (`PublicBaseUrl`), not part of the image.

## 1. Set up from zero

Prerequisites: an Azure subscription you own, the Azure CLI (`az login`), the GitHub CLI (`gh auth login`),
and the GitHub environments `staging` and `production` (the latter with a required reviewer).

```powershell
./scripts/azure-bootstrap.ps1            # region: -Location polandcentral (default)
```

The script is idempotent and creates nothing that costs money: resource providers, three resource groups,
two deployment identities with federated credentials (OIDC), per environment an Entra group
`sg-bajkowecuda-sql-admins-<env>` (you + the migration identity `id-bajkowecuda-migrator-<env>`), and the GitHub
secrets the workflows read (`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`; per environment `AZURE_CLIENT_ID` and
`SQL_ADMIN_OBJECT_ID`, the group). These are identifiers, not credentials: GitHub never holds an Azure secret.

Then deploy for the first time: merge to `main`, or run **Actions → CD → Run workflow**. The first run creates
the registry, pushes the image and creates every resource of the environment (about 10 minutes).

## 2. Deploy

Nothing to do: every merge to `main` runs CI, then CD.

1. **Build and push** the image to the registry.
2. **Staging**, in this order:
   1. Bicep deployment with the new image for the migration job only; the app keeps running the current image.
   2. The job `caj-bajkowecuda-migrate-<env>` runs `migrate`: EF Core migrations, then it makes sure the app identity
      has a database user with `db_datareader` + `db_datawriter`. If it fails, the deployment stops and the app
      still runs the previous build.
   3. Bicep deployment with the new image for the app, wait until `/api/version` reports the new commit, smoke test
      (including `/health/ready`, which opens a database connection as the app identity).
3. **Production**: waits for approval (**Actions → the CD run → Review deployments**), then the same steps
   with the same image digest.

Check what is running: `https://<app>/api/version` returns the git commit.

### Database access

Who can do what on the Azure SQL servers (Entra authentication only, no SQL passwords):

| Principal | Access |
|---|---|
| Group `sg-bajkowecuda-sql-admins-<env>`: you and `id-bajkowecuda-migrator-<env>` | Server administrator |
| App identity `id-bajkowecuda-<env>` | Database user, read and write data only (created by the migration job) |

To query the database yourself: allow your IP address on the server (**SQL server → Networking**, remove the rule
afterwards), then connect with Entra authentication, e.g. `sqlcmd -S <server>.database.windows.net -d BajkoweCuda
--authentication-method ActiveDirectoryDefault` after `az login`.

Run a migration by hand (normally the pipeline does it):

```bash
az containerapp job start -g rg-bajkowecuda-<env> -n caj-bajkowecuda-migrate-<env>
az containerapp job execution list -g rg-bajkowecuda-<env> -n caj-bajkowecuda-migrate-<env> -o table
```

Migration logs: workspace `log-bajkowecuda-shared`, table `ContainerAppConsoleLogs_CL`,
filter `ContainerJobName_s == "caj-bajkowecuda-migrate-<env>"`.

## 3. Roll back

**Fastest (seconds): activate the previous revision.** Container Apps keeps the last inactive revisions.

```bash
az containerapp revision list -g rg-bajkowecuda-prod -n ca-bajkowecuda-prod -o table
az containerapp revision activate -g rg-bajkowecuda-prod -n ca-bajkowecuda-prod --revision <previous-revision>
az containerapp ingress traffic set -g rg-bajkowecuda-prod -n ca-bajkowecuda-prod --revision-weight <previous-revision>=100
```

This is a stopgap: the next pipeline run deploys the newest image again. Follow it with one of the two below.

**Proper: redeploy a previous image digest.** **Actions → CD → Run workflow**, put the digest
(`sha256:...`) into `image_digest`. The pipeline skips the build and promotes that image through staging and
production as usual. Find digests in the summary of earlier CD runs, or:

```bash
az acr manifest list-metadata --registry <registry> --name bajkowecuda --orderby time_desc -o table
```

**Permanent: revert the commit** on `main` (`git revert`), which builds and deploys a new image.

Database changes are not rolled back by any of these: redeploying an older digest runs that image's migrations,
which finds nothing to apply and leaves the newer schema in place. Migrations must therefore stay backwards
compatible with the previous image (expand first, remove old columns in a later release).

## 4. Costs and budget

Each environment has a monthly budget (`monthlyBudget` in `infra/main.*.bicepparam`) that notifies the
subscription Owners at 50 %, 80 % and 100 %. A budget only notifies; it does not stop resources.

What costs money while idle: the container registry (Basic) and the production replica (always on).
Staging scales to zero. Both databases use the free serverless offer and pause when the monthly free
allowance is used up.

To move the production database to the always-on **Basic (DTU)** tier, set `sqlUseFreeOffer = false` in
`infra/main.prod.bicepparam`. Azure cannot convert a free-offer database in place: export the data first,
delete the database, deploy, then import. Do it before real data exists, or plan a maintenance window.

## 5. Routine operations

| Task | How |
|---|---|
| Change texts, prices, pictures | Edit `src/frontend/src/app/content/`, run `npm run i18n:extract`, translate, merge |
| Change the public address (custom domain) | Set `publicBaseUrl` in the environment's `.bicepparam`, merge. Pages, `robots.txt` and `sitemap.xml` follow |
| Read logs | Application logs and traces: Application Insights `appi-bajkowecuda-<env>`. Container console logs: workspace `log-bajkowecuda-shared`, table `ContainerAppConsoleLogs_CL`, filter `ContainerAppName_s == "ca-bajkowecuda-<env>"`; or Container App → Log stream |
| Preview infrastructure changes | Open a PR that touches `infra/`: the *Infrastructure CI* workflow runs a what-if against staging (see its job log) |
| Clean the registry | Weekly *Cleanup* workflow; it never deletes an image that staging or production is running |
| Add a secret | `az keyvault secret set --vault-name <vault> ...`; the app identity can read secrets, nobody else is granted access by the templates |

### Email

Account emails (confirmation, password reset, notices) go through the `EmailOutbox` table: the request only queues a
row, and a background dispatcher in the app sends it through Azure Communication Services (Mailpit locally,
UI on http://localhost:8025). A failed send is retried after 30 s, 1 min, 2 min ... up to 2 hours; after 8 attempts
the row becomes `Dead`. The dispatcher reads the table only when an email is queued or a retry is due, so an idle
site lets the serverless database pause.

| Question | How |
|---|---|
| Was an email sent? | App logs: `Sent ConfirmEmail email to a***@example.com` (addresses are masked) |
| Emails that gave up | `SELECT Id, Template, Attempts, LastError, CreatedAt FROM EmailOutbox WHERE Status = 'Dead'` (see *Database access*) |
| Send a dead email again | `UPDATE EmailOutbox SET Status = 'Pending', Attempts = 0, SendAfter = SYSUTCDATETIME() WHERE Id = '<id>'`, then restart the app revision (or queue any email) to wake the dispatcher |

The sender is `DoNotReply@<azure-managed-domain>` until a custom domain exists (Phase 8). The app identity sends with
the "Communication and Email Service Owner" role on the Communication Services resource.

### Admins and 2FA

Admins are the accounts whose **confirmed** email is listed in the GitHub secret `ADMIN_EMAILS` of the environment
(comma-separated). Nobody becomes an admin through registration.

```bash
gh secret set ADMIN_EMAILS --env staging --body "owner@example.com"
gh secret set ADMIN_EMAILS --env production --body "owner@example.com"
```

The next deployment applies it: the migration job grants the role to listed confirmed accounts and removes it from
everybody else. A listed person who registers later gets the role when they confirm their email or sign in.
An admin must set up 2FA (authenticator app) before the admin pages work, and keeps the recovery codes safe.

Lost authenticator and recovery codes: reset 2FA in the database (see *Database access*), then the admin signs in with
the password and sets up 2FA anew. The new security stamp ends the admin's open sessions.

```sql
DECLARE @id uniqueidentifier = (SELECT Id FROM AspNetUsers WHERE NormalizedEmail = UPPER('<email>'));
DELETE FROM AspNetUserTokens WHERE UserId = @id;
UPDATE AspNetUsers SET TwoFactorEnabled = 0, SecurityStamp = REPLACE(CONVERT(varchar(36), NEWID()), '-', '') WHERE Id = @id;
```

### Audit log and rate limits

Admin actions and security events (sign-ins, failed sign-ins, 2FA, password changes, export, deletion, role changes)
are in the `AuditLog` table; admins read them through `GET /api/admin/audit`. Too many requests answer
`429 rate_limited` with `Retry-After`: sign-in and similar 10/min per IP, emails 3/hour per IP and per address. Limits
are per replica, in memory, and reset on restart.

### Sessions and Data Protection keys

Session cookies, antiforgery tokens and the links in emails are protected by ASP.NET Core Data Protection keys. In
Azure the key ring is the blob `dataprotection/keys.xml` in the environment's storage account, wrapped by the Key Vault
key `dataprotection`. Deleting the blob or the key signs everybody out and invalidates links already sent; do not
delete them. ASP.NET Core rotates the keys in the ring every 90 days by itself.

## 6. Not automated yet

- **Alerts, availability test, restore drill**: later phases (docs/PLAN.md §15).
- **Custom domain and email domain**: Phase 8, once a domain exists.
