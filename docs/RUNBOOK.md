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

The container registry is shared (`rg-bajkowecuda-shared`). The image is **built once**; the same digest goes to
staging and then to production. The public origin is configuration (`PublicBaseUrl`), not part of the image.

## 1. Set up from zero

Prerequisites: an Azure subscription you own, the Azure CLI (`az login`), the GitHub CLI (`gh auth login`),
and the GitHub environments `staging` and `production` (the latter with a required reviewer).

```powershell
./scripts/azure-bootstrap.ps1            # region: -Location polandcentral (default)
```

The script is idempotent and creates nothing that costs money: resource providers, three resource groups,
two deployment identities with federated credentials (OIDC) and the GitHub secrets the workflows read
(`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `SQL_ADMIN_OBJECT_ID`, and `AZURE_CLIENT_ID` per environment).
These are identifiers, not credentials: GitHub never holds an Azure secret.

Then deploy for the first time: merge to `main`, or run **Actions → CD → Run workflow**. The first run creates
the registry, pushes the image and creates every resource of the environment (about 10 minutes).

## 2. Deploy

Nothing to do: every merge to `main` runs CI, then CD.

1. **Build and push** the image to the registry.
2. **Staging**: Bicep deployment, wait until `/api/version` reports the new commit, smoke test.
3. **Production**: waits for approval (**Actions → the CD run → Review deployments**), then the same steps
   with the same image digest.

Check what is running: `https://<app>/api/version` returns the git commit.

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

Database changes are not rolled back by any of these. From Phase 3 on, migrations must stay backwards
compatible with the previous image for exactly this reason.

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
| Read logs | Log Analytics workspace `log-bajkowecuda-<env>`, table `ContainerAppConsoleLogs_CL`; traces in Application Insights `appi-bajkowecuda-<env>` |
| Preview infrastructure changes | Open a PR that touches `infra/`: the *Infrastructure CI* workflow runs a what-if against staging (see its job log) |
| Clean the registry | Weekly *Cleanup* workflow; it never deletes an image that staging or production is running |
| Add a secret | `az keyvault secret set --vault-name <vault> ...`; the app identity can read secrets, nobody else is granted access by the templates |

## 6. Not automated yet

- **Database access for the app identity.** The SQL servers accept Entra identities only and their administrator is
  the person who ran the bootstrap. Before the app first uses the database (Phase 3) the app identity must be added
  as a database user (`CREATE USER [id-bajkowecuda-<env>] FROM EXTERNAL PROVIDER`).
- **Migration job, alerts, availability test, restore drill**: later phases (docs/PLAN.md §15).
- **Custom domain and email domain**: Phase 8, once a domain exists.
