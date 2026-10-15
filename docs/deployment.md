# Deployment runbook

The workflow points at this document when the site fails its health check
after a deploy. Read [Rolling back](#rolling-back) first if that is why you are
here.

---

## The target

Everything in the table exists. It was created on 21 September 2026.

| | |
|---|---|
| Hosting | Azure App Service, Linux, .NET 10, plan `theebuhle-plan` (**B1**) |
| Region | **South Africa North** |
| App | `theebuhle-foundation`: <https://theebuhle-foundation.azurewebsites.net> |
| Resource group | `theebuhle-rg` |
| Database | Azure SQL server `theebuhle-sql`, database `theebuhle`: serverless, on the **free offer** |
| Database sign-in | **Microsoft Entra only.** There is no SQL password. The app connects as its managed identity; the pipeline as its federated identity |
| Domain | `theebuhlefoundation.co.za` (registrar and DNS: Afrihost) |
| Subscription | Azure for Students. Its policy allows only a handful of regions, South Africa North among them |

The region is a POPIA decision before it is a latency one, although a Vaal
mobile connection notices both.

### Why B1 and no staging slot

Deployment slots need the **Standard** tier (about US$70 a month). On a
student subscription with US$100 of credit, that is most of the budget for one
feature. B1 (about US$13 a month) runs the app comfortably for an organisation
of this size. What it costs:

- **A short restart during each deploy.** A few seconds of 503s.
- **A slower rollback.** Redeploying the previous commit, about five minutes,
  instead of swapping back in seconds.

When the foundation pays for its own hosting, scale the plan to S1, create a
`staging` slot, and put the deploy-to-slot, health-check and swap steps back in
`deploy.yml`.

### Why the database has no password

A password is something to store, rotate, leak and lose. With Entra-only
authentication:

- the web app connects as its **system-assigned managed identity**, a user
  called `theebuhle-foundation` in the database with `db_datareader` and
  `db_datawriter`, and nothing else;
- the pipeline connects as its **federated identity**, which also has
  `db_ddladmin` so it can apply migrations;
- people connect with their own Microsoft account, and the server admin is the
  account that created it.

The connection string holds no secret at all:

```
Server=tcp:theebuhle-sql.database.windows.net,1433;Database=theebuhle;
Authentication=Active Directory Managed Identity;Encrypt=True;
```

---

## How it was set up

Recorded so it can be repeated in another subscription. It does not need
doing again here.

### 1. Resources

```bash
az group create --name theebuhle-rg --location southafricanorth

az appservice plan create --name theebuhle-plan --resource-group theebuhle-rg \
  --location southafricanorth --sku B1 --is-linux

az webapp create --name theebuhle-foundation --resource-group theebuhle-rg \
  --plan theebuhle-plan --runtime "DOTNETCORE:10.0" --assign-identity [system]

# Once per subscription: a new student subscription has never used Azure SQL.
az provider register --namespace Microsoft.Sql --wait

az sql server create --name theebuhle-sql --resource-group theebuhle-rg \
  --location southafricanorth --enable-ad-only-auth \
  --external-admin-principal-type User \
  --external-admin-name <your sign-in> --external-admin-sid <your object id>

az sql db create --name theebuhle --server theebuhle-sql --resource-group theebuhle-rg \
  --edition GeneralPurpose --compute-model Serverless --family Gen5 --capacity 2 \
  --use-free-limit --free-limit-exhaustion-behavior AutoPause
```

`AutoPause` means the database **pauses** rather than starts charging if the
monthly free allowance runs out.

### 2. Firewall

```bash
az sql server firewall-rule create --server theebuhle-sql --resource-group theebuhle-rg \
  --name allow-azure-services --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
```

Add your own address only while you need it, and remove it afterwards:

```bash
az sql server firewall-rule delete --server theebuhle-sql --resource-group theebuhle-rg \
  --name <rule name>
```

### 3. Schema and the app's database user

The first schema was applied from a developer machine, signed in with
`az login`:

```bash
dotnet ef database update --project src/TheeBuhle.Infrastructure \
  --startup-project src/TheeBuhle.Api \
  --connection "Server=tcp:theebuhle-sql.database.windows.net,1433;Database=theebuhle;Authentication=Active Directory Default;Encrypt=True;"
```

Then, connected to the database as the Entra admin:

```sql
CREATE USER [theebuhle-foundation] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [theebuhle-foundation];
ALTER ROLE db_datawriter ADD MEMBER [theebuhle-foundation];
```

From then on, the pipeline applies migrations.

### 4. Application settings

Already set: `ASPNETCORE_ENVIRONMENT=Production`, the `TheeBuhle` connection
string above, HTTPS only, TLS 1.2 minimum, FTP off, and `/health/live` as the
health-check path. **Always On is off**, deliberately; see
[Keeping the database free](#keeping-the-database-free).

**The secrets are set by a person, never committed and never pasted into a
chat.** Double underscores, because App Service cannot store a colon:
`Jwt__SigningKey` sets `Jwt:SigningKey`.

| Setting | Notes |
|---|---|
| `Jwt__SigningKey` | At least 32 random bytes. **The app refuses to start without it.** |
| `Signing__SigningKey` | A **different** key, for signed download URLs. |
| `Smtp__Host`, `Smtp__Username`, `Smtp__Password` | The client's SMTP at Afrihost. Until these are set, no email is sent. |
| `PayFast__MerchantId`, `PayFast__MerchantKey`, `PayFast__Passphrase` | From the client's PayFast account. |
| `PayFast__UseSandbox` | `false` only once the three values above are the client's real ones. |
| `Section18A__PboNumber`, `Section18A__NpoNumber` | The foundation's real registration numbers. A wrong one lands on a donor's tax return. **Until both are set, the site states no registration numbers, offers no Section 18A certificate, and issues none.** Setting them turns all of that on, with no code change. |
| `Safeguarding__Phone` | The foundation's own safeguarding line, written as it should appear, e.g. `+27 16 000 0000`. **Until it is set, the Safeguarding and Contact pages show no foundation number, only Childline South Africa (116).** Childline stays on the pages once it is set, too. |

Both signing keys can be generated and set in one command. Neither key is
printed, and neither lands in shell history:

```bash
az webapp config appsettings set --name theebuhle-foundation --resource-group theebuhle-rg \
  --settings "Jwt__SigningKey=$(openssl rand -base64 48)" "Signing__SigningKey=$(openssl rand -base64 48)" \
  --output none
```

Changing either key later signs everybody out, which is the correct result if
a key has leaked.

### 5. The first administrator

Production starts with **no accounts**; the seeder is Development-only. Create
the first administrator from a machine that can reach the database. That means
signed in with `az login`, with its address allowed on the SQL firewall:

```bash
ConnectionStrings__TheeBuhle="Server=tcp:theebuhle-sql.database.windows.net,1433;Database=theebuhle;Authentication=Active Directory Default;Encrypt=True;" \
dotnet run --project src/TheeBuhle.Api --no-launch-profile -- create-admin \
  --email <address> --first <first name> --surname <surname> --title Director
```

The password is typed at a hidden prompt (at least 12 characters, twice). The
command **refuses to run once any administrator exists**, so it cannot be used
later to get round the console's access control. Everybody else is added from
the staff console's **People** section, which gives each person a one-time code
to choose their own password with (see `docs/security.md`, "Accounts: invite-only").

### Demo data

For the presentation, the live database holds the same demonstration data as
Development: programmes, learners, marks, registers, news, donations and one
account per role. It was loaded on 21 September 2026 with:

```bash
ConnectionStrings__TheeBuhle="<the Active Directory Default connection string above>" dotnet src/TheeBuhle.Api/bin/Debug/net10.0/TheeBuhle.Api.dll seed-demo
```

The demo accounts' password is typed at a hidden prompt, **not** the one in the
README. The command refuses to run on a database that already has
programmes, so it cannot mix invented learners into real records. Before the
foundation uses the site for real, create a fresh database rather than
deleting the demo rows by hand: consents and the audit log are append-only by
design, so they cannot be deleted.

### 6. The pipeline's identity

Done once the GitHub repository exists, because the federated credential names
it:

1. Create an app registration, for example `theebuhle-github-deploy`.
2. Add a federated credential: GitHub Actions, this repository, environment
   `production`.
3. Give its service principal **Contributor** on `theebuhle-rg`.
4. Make it a database user that can change the schema:

   ```sql
   CREATE USER [theebuhle-github-deploy] FROM EXTERNAL PROVIDER;
   ALTER ROLE db_datareader ADD MEMBER [theebuhle-github-deploy];
   ALTER ROLE db_datawriter ADD MEMBER [theebuhle-github-deploy];
   ALTER ROLE db_ddladmin  ADD MEMBER [theebuhle-github-deploy];
   ```

5. Put `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` into
   the repository's `production` environment secrets. They are identifiers,
   not passwords; there is no password to store, which is the point.

### 7. DNS

The domain's DNS is at **Afrihost**, and the client's email lives there too.
Before the change, `@` and `www` showed Afrihost's "Domain Registered" page, so
no existing site is replaced.

Add or change **only these four records** in Afrihost's DNS editor:

| Name | Type | Value |
|---|---|---|
| `@` | A | `102.133.154.33` |
| `www` | CNAME | `theebuhle-foundation.azurewebsites.net` (delete the old `www` A record first) |
| `asuid` | TXT | `1C209998947ACAB4282CB65A0B694CEFDF52FC33ACAD053859901D87C05D2757` |
| `asuid.www` | TXT | the same value |

The TXT records prove to Azure that whoever controls the domain agreed to
this. The value is the app's domain verification ID, not a secret.

**Leave everything else exactly as it is**: the `MX` record, the `mail`,
`webmail`, `autodiscover`, `autoconfig`, `cpanel` and `ftp` records, the SPF
and `mailconf` TXT records, and any SRV records. `mail` and `webmail` point at
**the same IP address** the website used to, so edit rows by name, never by
searching for `102.222.124.101`. Changing those takes the foundation's email
down.

Then bind both hostnames and let App Service issue free managed certificates:

```bash
for host in theebuhlefoundation.co.za www.theebuhlefoundation.co.za; do
  az webapp config hostname add --webapp-name theebuhle-foundation \
    --resource-group theebuhle-rg --hostname $host
  az webapp config ssl create --name theebuhle-foundation \
    --resource-group theebuhle-rg --hostname $host
done
```

Propagation depends on the records' TTL, which is 7200 seconds (two hours) at
Afrihost.

**Done on 22 September 2026.** Both names are bound, with App Service managed
certificates (issued by GeoTrust, valid until March 2027, renewed
automatically), and `http://` redirects to `https://`. The mail records were
checked afterwards and were unchanged.

**One follow-up in Afrihost's DNS editor:** the SPF record reads
`v=spf1 include:spf.aserv.co.za +a +mx -all`. The `+a` allows whatever server
the domain points to to send the foundation's email. That is now Azure's
address, which App Service shares between many customers. The site sends
email through Afrihost's SMTP, never from there, so change the record to:

```
v=spf1 include:spf.aserv.co.za +mx -all
```


---

## Routine deployment

Merge to `main`. That is the whole procedure.

Watch the Actions tab. The run takes roughly six minutes and fails loudly if
anything goes wrong. The site is only touched after the build, the tests and
the migration have all succeeded.

---

## Rolling back

### The code, not the schema

```
Actions → Deploy → Run workflow → (the last good commit)
  skip_migration: true
```

The migration is skipped and the previous code is published, then
health-checked. Every migration has to be backwards-compatible with the code
before it (see the header of `deploy.yml`), so the previous code runs against
the newer schema.

### The schema

**By hand, deliberately.** Get the script that ran from the workflow artefact
(`migration-sql`), read it, and write the reverse. `dotnet ef migrations script
<from> <to>` will generate a down script, but read it before running it: EF's
idea of reversing a column drop is to recreate an empty column, which is not the
same as getting the data back.

Restore from a point-in-time backup if data was lost. Azure SQL keeps them
automatically. **Test a restore before you need one**:

```bash
az sql db restore --dest-name theebuhle-restore-test --name theebuhle \
  --server theebuhle-sql --resource-group theebuhle-rg \
  --time "<an ISO 8601 time in the last 7 days>"
```

Then check it has the data, and delete it.

---

## When the site is unhealthy after a deploy

1. **Read `/health`**: <https://theebuhle-foundation.azurewebsites.net/health>.
   It reports the database check separately, which distinguishes "the app will
   not start" from "the app cannot reach SQL".
2. **Read the logs**:
   `az webapp log tail --name theebuhle-foundation --resource-group theebuhle-rg`.
3. The common causes, in the order they actually happen:
   - **a missing application setting.** A missing signing key stops the app at
     startup with a message naming the setting;
   - **the app's identity missing from the database** (`Login failed for user
     '<token-identified principal>'`). Re-run the `CREATE USER` in step 3;
   - **a migration that applied but needs data the code has not written yet.**
4. If it cannot be fixed quickly, roll back the code as above.

---

## Health and monitoring

| Endpoint | Checks | Polled by |
|---|---|---|
| `/health` | The process **and** the database | The deploy pipeline, and people |
| `/health/live` | The process only | App Service's own health check, every minute |

Nothing else is configured. Paid alerting (Azure Monitor alert rules,
Application Insights) is left out on purpose: the project runs on a student
subscription with no budget. Once the repository is on GitHub, a scheduled
workflow that checks `/health/live` is the free way to get an email when the
site goes down.

---

## Keeping the database free

The database uses Azure SQL's free offer: **100,000 vCore-seconds a month**.
When they run out, the database **pauses until the next month** rather than
charging (`AutoPause`). A paused-for-the-month database means the whole site
is down, so the allowance is the real limit on this hosting.

What uses it is time **awake**. The database wakes on the first query and
pauses after **60 minutes** with no queries. The free offer does not allow a
shorter delay. Each wake-up therefore costs at least about 1,800 vCore-seconds
(0.5 vCore for an hour), which is **roughly 55 wake-ups, or 55 hours awake, a
month**.

Two settings exist to protect that budget, and both look like mistakes if you
don't know why:

- **App Service's health check polls `/health/live`, not `/health`.** Polling
  the database every minute would keep it awake permanently and use up the
  month's allowance in about three days. This happened for a few hours on
  21 September 2026 before it was noticed.
- **Always On is off.** Always On requests the home page every few minutes,
  and the home page reads its figures from the database. The health-check
  poll already keeps the app process warm, so Always On added nothing but
  database wake-ups.

To see what is left: Azure portal → the `theebuhle` database → **Metrics** →
`Free amount remaining`. Or:

```bash
az monitor metrics list --resource <database resource id> --metric free_amount_remaining --interval PT1H
```

Before a presentation, open the site about five minutes early: the first
request after a pause waits for the database to resume, typically under a
minute.

When the foundation moves to a paid tier, turn Always On back on, and the
health check can go back to `/health`.

---

## Drills

Carried out on the live deployment, so the procedures above are known to work
rather than assumed to.

### Rollback: 21 September 2026

A deliberately broken release (the real build with the JWT issuer blanked in
`appsettings.json`, a realistic configuration mistake) was deployed over the
working one.

| Time (UTC) | |
|---|---|
| 14:40 | Healthy before the drill |
| 14:51 | Broken release live; `/health` stopped answering |
| 14:51 | Rollback started: the previous build redeployed |
| by 14:58 | Healthy again |

**A bad release was reversed in under seven minutes** by redeploying the
previous build. Note that `az webapp deploy` kept waiting about eleven minutes
after each upload, even with `--async true`, so time a rollback by polling
`/health`, not by waiting for the command to return.

### Database restore: 21 September 2026

A point-in-time restore of the live database, as it was at 14:25:22 UTC, into
a separate database, `theebuhle-restore-test`, on the Basic tier. The live
database was not touched.

| | |
|---|---|
| Requested | 14:30 UTC |
| Online | 15:16 UTC: **about 46 minutes** (2,741 s) |
| Tables compared | 44 |
| Row counts | **identical in every table**, 304 rows in total |
| Triggers | both present (`trg_consents_append_only`, `trg_audit_log_immutable`) |

The copy was deleted once compared. Two things learned:

- **Allow most of an hour.** The restore time is set by Azure's backup
  processing, not by the database's size. This one is tiny and still took 46
  minutes. Anybody planning an outage around a restore should plan for that.
- **The CLI can go quiet.** `az sql db restore` showed nothing until the
  database was online, and while a restore is in progress the new name is
  reserved: a second attempt fails with `DatabaseNameInUse`. Watch the resource
  group's activity log instead of retrying.

To restore for real, restore to a new name as above, check it, then point the
app's `TheeBuhle` connection string at the new database. Don't restore over
the live one.
