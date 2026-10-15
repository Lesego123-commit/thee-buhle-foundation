# Thee Buhle Foundation — learner support platform

A web application, REST API and mobile app for **Thee Buhle Foundation**, a youth
development NPO in Vanderbijlpark in the Vaal. The foundation runs Saturday
tutoring, year-long mentorship, life-skills workshops and holiday camps for
learners in Grades 8 to 12.

Built for **INSY7315 — Information Systems 3E (Work Integrated Learning)**,
Task 2. The client is real, the requirements come from them, and the system is
built to be handed over rather than demonstrated once.

---

## What is here

| Part | Technology | Where |
|---|---|---|
| Public website | ASP.NET Core Razor Pages | [`src/TheeBuhle.Api/Pages`](src/TheeBuhle.Api/Pages) |
| Staff console | Razor Pages shell + vanilla JS against the API | [`Pages/Admin.cshtml`](src/TheeBuhle.Api/Pages/Admin.cshtml) |
| REST API | ASP.NET Core 10, 76 endpoints | [`src/TheeBuhle.Api/Controllers`](src/TheeBuhle.Api/Controllers) |
| Domain and rules | C# class library, no framework dependencies | [`src/TheeBuhle.Domain`](src/TheeBuhle.Domain), [`src/TheeBuhle.Application`](src/TheeBuhle.Application) |
| Data access | EF Core 10 on SQL Server 2022 | [`src/TheeBuhle.Infrastructure`](src/TheeBuhle.Infrastructure) |
| Mobile app | Android, Kotlin, XML layouts + Fragments | [`android/`](android) |
| CI/CD | GitHub Actions → Azure App Service | [`.github/workflows`](.github/workflows) |

Roughly 22 000 lines of C# across four projects, 16 public web pages, 11 Android
screens, and 309 automated tests.

---

## Running it

### You need

- **.NET 10 SDK** (`dotnet --version` should report 10.x)
- **SQL Server 2022** — Developer or Express edition, reachable on `localhost`
- **Android Studio** (2025.3 or newer) and **JDK 21**, for the mobile app only.
  Android Studio's bundled JetBrains Runtime is a JDK 21 and works as
  `JAVA_HOME`.

### The API and website

```bash
cd "part 2"
dotnet user-secrets set "ConnectionStrings:TheeBuhle" "Server=localhost;Database=TheeBuhleDev;Trusted_Connection=True;TrustServerCertificate=True" --project src/TheeBuhle.Api
dotnet run --project src/TheeBuhle.Api
```

On first run in Development the application creates the database, applies every
migration and seeds demonstration data. Then:

| | |
|---|---|
| Website | <http://localhost:5166> |
| Staff console | <http://localhost:5166/admin> |
| API documentation (Swagger) | <http://localhost:5166/swagger> |
| Health check | <http://localhost:5166/health> |

### Adding people

There is no public sign-up: accounts show children's records, so the foundation
decides who gets one. Staff add learners, parents (linked to their children),
mentors and, for administrators, staff in the console's **People** section. Each
person gets a one-time code, redeemed in the app ("I have a code") or at
`/activate`, where they choose their own password. A forgotten password is a
reset code issued the same way. Details and the security rules are in
[docs/security.md](docs/security.md).

### Signing in

The seeder creates these accounts. Locally, all of them use the password
`Vanderbijlpark2026!`, which exists only in the seeder and only runs in
Development.

**On the live site the same accounts exist, with a different password** that
the group shares privately. The live data was loaded with the `seed-demo`
command, which asks for the password at a hidden prompt precisely so that the
one printed here does not open a public staff console. See
[docs/deployment.md](docs/deployment.md#demo-data).

| Account | Role | What it shows |
|---|---|---|
| `admin@theebuhlefoundation.co.za` | Administrator | Everything, including the audit log |
| `safeguarding@theebuhlefoundation.co.za` | Safeguarding lead | The safeguarding queue — and nobody else can see it |
| `programmes@theebuhlefoundation.co.za` | Programme staff | Registers, learners, announcements; refused the two above |
| `thandi.mokoena@example.co.za` | Mentor | The mobile mentor screens |
| `nomvula.mahlangu@example.co.za` | Parent | One child's progress, and no other child's |

Signing in as programme staff and then as the safeguarding lead is the quickest
way to see the authorisation boundary that most of this system is about.

### The Android app

Open `part 2/android` in Android Studio and press Run, or from a terminal:

```bash
cd "part 2/android"
./gradlew :app:installDebug
```

A debug build points at **`http://10.0.2.2:5166/api/`**, which is how the
emulator reaches the API running on the host machine. The **`azure`** build
(`./gradlew :app:installAzure`) is the same app pointed at the live deployment,
<https://theebuhle-foundation.azurewebsites.net>; use it for the presentation.
A release build points at the client's domain. Both are set in [`app/build.gradle.kts`](android/app/build.gradle.kts)
rather than in Kotlin, so aiming a build at a laptop on the same Wi-Fi is a
one-line change.

Sign in with any of the accounts above. A learner, a parent and a mentor each
get a different app: a parent has no Buddies tab because the peer features are
learner-only, and a mentor gets Mentees and Alerts instead of Learn and
Progress.

---

## Tests

```bash
dotnet test tests/TheeBuhle.UnitTests          # 207 — domain rules, services, security
dotnet test tests/TheeBuhle.IntegrationTests   # 84  — the API over real HTTP, real SQL Server
cd android && ./gradlew :app:testDebugUnitTest # 18  — token refresh, error mapping, wire format
```

The integration tests need SQL Server running; each fixture creates its own
database, runs the real migrations and the real seeder, and drops it afterwards.
They are not mocked, and they are the tests that matter most here — the
authorisation rules, the safeguarding escalation and the rate limiter are all
asserted over HTTP rather than against a class.

`docs/testing.md` explains what each suite covers and why some of it is
deliberately shallow.

---

## Documentation

| Document | What it answers |
|---|---|
| [References](docs/references.md) | Harvard references: the legislation and standards implemented, documentation consulted, and third-party software |
| [Getting in](docs/access.md) | **Start here as a group member:** the live addresses, which account to use, and how to install the app |
| [Architecture](docs/architecture.md) | How the layers fit together, and the five design patterns |
| [Database](docs/database.md) | The 44 tables, the constraints, and what the schema refuses to store |
| [ERD](docs/erd.md) | Entity-relationship diagrams, generated from the live schema |
| [Accessibility](docs/accessibility.md) | WCAG 2.2 AA audit of the website and app, and what it changed |
| [Presentation](docs/presentation.md) | Slide outline mapped to the presentation rubric, and the demo script |
| [API reference](docs/api.md) | Every endpoint, who may call it, and the error shape |
| [Security](docs/security.md) | Authentication, authorisation, POPIA and child safeguarding |
| [DevOps](docs/devops.md) | The three workflows and what each one gates |
| [Deployment](docs/deployment.md) | The runbook: first deployment, routine deployment, and rolling back |
| [Testing](docs/testing.md) | What is tested, at which level, and what is not |
| [Traceability](docs/traceability.md) | Task 1 requirements mapped to the code that implements them |

---

## A note on the data

Everything in the seeded database is invented. The learners, the parents, the
marks and the attendance registers are illustrative, and the site says so in its
own footer. The client's real learner data has never been in this repository and
should not be until the foundation has signed off the handover.

The one thing that is real is the organisation: the name, the address, the
programmes and the safeguarding policy come from the client.

---

## Repository layout

```
part 2/
├── src/
│   ├── TheeBuhle.Domain/          entities, enums, repository interfaces, domain events
│   ├── TheeBuhle.Application/     services, access policy, listeners, escalation chain
│   ├── TheeBuhle.Infrastructure/  EF Core, repositories, mail, payments, notifications
│   └── TheeBuhle.Api/             controllers, middleware, Razor Pages, wwwroot
├── tests/
│   ├── TheeBuhle.UnitTests/
│   └── TheeBuhle.IntegrationTests/
├── android/                       Kotlin, XML layouts, Fragments, MVVM
├── docs/
└── .github/workflows/             ci.yml, deploy.yml, security.yml
```

References point inward only: `Api → Infrastructure → Application → Domain`, and
the Domain references nothing. A rule in the Domain cannot depend on EF Core,
which is what lets the unit tests run without a database.

---

## Deployment

The `main` branch deploys to **Azure App Service** in **South Africa North**,
with **Azure SQL** in the same region. The pipeline builds, tests, applies the
migration, deploys, and checks `/health`. It runs on a B1 plan without a
staging slot, to fit a student subscription; see
[docs/deployment.md](docs/deployment.md). The live app is
<https://theebuhle-foundation.azurewebsites.net>.

Data stays in South Africa. That is a POPIA consideration rather than a
performance one, although a Vaal mobile connection notices the difference too.

The domain `theebuhlefoundation.co.za` stays with Afrihost for email — the MX
records and the mail subdomains are untouched — and only the `A` record moves to
Azure. The client's mailboxes keep working throughout.

Secrets are GitHub repository secrets, set by the team, and are never in this
repository. See [docs/deployment.md](docs/deployment.md) for the list and the
runbook.

---

## Known limitations

Stated plainly, because a marker will find them anyway and a client deserves to
know before handover:

- **The API still sends push notifications through Expo**, which belonged to the
  retired React Native app (deleted, with the PHP reference build, on
  22 September 2026). The Kotlin app never registers with Expo, so that path
  delivers nothing — see the next point.
- **Push notifications are not implemented in the Android app.** The API queues
  notifications and the app reads them in the Alerts tab, but there is no
  Firebase Cloud Messaging integration, so nothing arrives while the app is
  closed. That needs a Firebase project the client owns.
- **The payment gateway is PayFast's sandbox.** Switching to live is a
  configuration change plus the client's own merchant credentials; no code
  changes.
- **There is no photography.** The site is designed around coloured placeholders
  on purpose, so it looks deliberate until the client supplies images.
- **Email is configured but unproven at volume.** MailKit sends through the
  domain's own SMTP; a bulk announcement to two hundred parents has been tested
  against a local server, not against Afrihost's sending limits.

---

*Built by the INSY7315 group project team for Thee Buhle Foundation, 2026.*
