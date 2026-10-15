# DevOps

Three GitHub Actions workflows. Each has one job to do and fails loudly when it
cannot do it.

| Workflow | Runs on | Gates |
|---|---|---|
| [`ci.yml`](../.github/workflows/ci.yml) | push and PR to `main` or `develop` | Build, unit tests, migration drift, integration tests, Android build and tests |
| [`deploy.yml`](../.github/workflows/deploy.yml) | push to `main`, or manually | Everything in CI again, then Azure |
| [`security.yml`](../.github/workflows/security.yml) | push to `main`, and 03:00 UTC Mondays | CodeQL, vulnerable packages, secrets |

All three declare `permissions: contents: read`. The repository default can be
write, and a workflow that does not need write access should not have it — a
compromised action cannot push to a branch it cannot write.

Concurrency is set per branch with `cancel-in-progress`, so a new push
supersedes the run in progress. The only result that matters is the one for the
latest commit.

---

## CI

### `build-and-test`

1. Restore and build with **`-warnaserror`**. The solution sets
   `TreatWarningsAsErrors` and `AnalysisLevel=latest-recommended`, so an
   analyser warning fails the build rather than accumulating.
2. **207 unit tests**, with results uploaded as an artefact even on failure —
   the run you need to read is the one that failed.
3. **`dotnet ef migrations has-pending-model-changes`**. Fails if the EF model
   and the migrations have drifted.
4. Publish the API and upload it.

The drift check is there because the failure it catches is silent. The code
compiles, the tests pass against a database somebody created by hand, and the
first deployment onto a fresh database is the one that breaks.

### `integration`

Runs **SQL Server 2022 as a service container**, waits for it to accept
connections, and runs the **84 integration tests** against it. Not an in-memory
provider and not SQLite: filtered unique indexes, check constraints and the
retrying execution strategy only behave like themselves on the real engine, and
those are exactly the things worth testing.

### `android`

JDK 21 (Temurin, matching the JetBrains Runtime that Android Studio bundles),
Gradle with its cache, then Android lint, **18 unit tests**, and
`assembleDebug`.

Lint runs *before* the tests and fails the job rather than filing a warning: an
accessibility or resource problem is cheaper to read from a report than to find
on a phone.

The debug APK is uploaded as an artefact, so a marker or the client can install
the build CI verified rather than one somebody assembled by hand.

---

## Deploy

Two jobs, and the second `needs` the first.

### `gate`

Builds and runs the unit tests and the drift check **again**, on the merge
commit. This exists because of a bug in an earlier version of this pipeline: the
workflow was named as though it waited for CI and did not, so a red CI run and a
green deployment could happen from the same commit. A deployment that does not
re-verify is a deployment that trusts a check that may never have run.

### `deploy`

Targets the `production` environment, which is where a required reviewer can be
configured in GitHub.

1. **Sign in to Azure** with an OIDC federated credential. No stored password;
   the job requests a short-lived token and the subscription trusts this
   repository and this environment.
2. **Generate the migration script** — `--idempotent`, so it can be applied to a
   database in any state — and upload it as an artefact. What ran against
   production is recoverable afterwards.
3. **Apply the migration** before deploying the new code, so the schema is ahead
   of the application rather than behind it.
4. **Publish and deploy** to the App Service.
5. **Check `/health`**, retrying for up to five minutes while the app starts.
   A failure stops the workflow with an error pointing at the runbook. There
   is deliberately no automatic rollback: somebody should look first.
6. **Sign out of Azure**, always.

There is no staging slot. Slots need the Standard tier, which would spend most
of the student subscription's credit; the site runs on B1. The trade-off and
the path back to slot-and-swap are in [deployment.md](deployment.md#why-b1-and-no-staging-slot).

---

## Secrets

Set as **GitHub repository secrets** by the team. None of them is in this
repository, and the values come from the client.

| Secret | Used for |
|---|---|
| `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | OIDC sign-in, and through it the migration: the database accepts only Entra sign-ins, so there is no SQL user or password to store. Identifiers, not credentials. |

Application settings — the JWT signing key, the connection string, the SMTP
credentials, the PayFast merchant details — live in **App Service
configuration**, not in the repository and not in the workflow. They are set
once, by the team, in the Azure portal.

`.gitleaks.toml` allow-lists nothing that is a real secret: what remains covers
development placeholders such as the design-time connection string. The entry
for the PHP reference implementation went with that directory on
22 September 2026.

---

## Branching

`main` is deployable. `develop` is where work lands. CI runs on both; deployment
runs on `main` only.

Pull requests are how work reaches `main`, so the CI run on the merge is the one
that gates a deployment.

---

## What is not automated

- **Rolling back a migration.** The pipeline can redeploy previous code in
  minutes (`workflow_dispatch` with `skip_migration`), but a schema change is
  reversed by hand, deliberately. See [deployment.md](deployment.md).
- **Database backups.** Azure SQL's automatic backups are on by default with
  point-in-time restore; nothing in this repository configures or tests them.
  Somebody should test a restore before the client depends on it.
- **Seeding production.** The seeder runs in Development only. Production starts
  empty and the client's real data is entered through the staff console.
