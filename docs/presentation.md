# Task 2 presentation

A slide outline and demo script for the 60-mark presentation. Each section
names the rubric criterion it earns marks against, so every slide has a reason
to be there. Timings assume about 15 minutes. Scale them if the slot differs.

| Criterion | Marks | Where it is earned |
|---|---|---|
| Technical demo | 20 | Slides 5–6, the live demo |
| Architecture & system design | 15 | Slides 3–4 |
| GitHub & version control | 10 | Slide 7 |
| Requirements alignment | 5 | Slides 2 and 8 |
| Communication & professionalism | 10 | Throughout: see "Delivery" at the end |

---

## Slide outline

### 1. Title (0:30)
Thee Buhle Foundation: a youth-programme platform for a real NPO in
Vanderbijlpark. Group members and roles. One sentence: *"Staff run
programmes, parents follow their child, mentors check in, and nobody sees a
child's data they have no business seeing."*

### 2. The client's problem (1:00) · *Requirements*
Who the foundation serves (learners in the Vaal, their parents, volunteer
mentors) and the three things they asked for: a public website that
brings in volunteers and donors, one place for staff to run programmes, and a
way for parents and mentors to follow learners. POPIA and child safeguarding
as the constraint over all of it.

### 3. Architecture (2:00) · *Architecture*
- One diagram: the Razor Pages website and staff console, the Android app,
  one ASP.NET Core API on .NET 10, Azure SQL, all on Azure App Service in
  South Africa North.
- The four layers, Domain → Application → Infrastructure → Api, and **why a
  monolith**: one small NPO, one deployment, no team to run microservices
  (`docs/architecture.md`).
- The five patterns, one line each: Repository + Unit of Work, Observer
  (domain events), Strategy (notification channels), **Chain of
  Responsibility (safeguarding escalation)**, MVVM (Android).

### 4. Data design (1:30) · *Architecture*
- Show `docs/erd/core.png`: the 16-table core model.
- 43 tables, 62 foreign keys, filtered unique indexes, check constraints.
- The two that matter: **consents are append-only** and **the audit log is
  immutable**, and the database refuses otherwise through triggers. Explain
  why: POPIA needs "what did the parent agree to, and when did they change
  their mind?" to be answerable years later.

### 5–6. Live demo (6:00) · *Technical demo*
See the demo script below.

### 7. How we worked (1:30) · *GitHub & version control*
- Branching: `develop` is where work lands, `main` is deployable, and pull
  requests move work to `main`. Show a real PR with its review and green checks.
- Three workflows: `ci.yml` (build, 207 unit tests, migration drift check, 84
  integration tests against real SQL Server, Android build and 18 tests),
  `deploy.yml` (re-runs CI, applies the migration, deploys to Azure),
  `security.yml` (CodeQL, vulnerable packages, secret scanning, weekly).
- Secrets live in GitHub repository secrets and Azure OIDC, never in the
  repository.
- Show the commit history per member. *Each member should be able to point at
  their own commits.*

### 8. Requirements traceability & what is next (1:00) · *Requirements*
- `docs/traceability.md`: each client requirement → the screen → the endpoint
  → the test that proves it.
- Honest limits: production backups are Azure's defaults and a restore has not
  been rehearsed; donations go through PayFast, but only its sandbox has been
  used until the client's merchant account is connected.
- Accessibility: WCAG 2.2 AA audit, findings fixed (`docs/accessibility.md`).

### 9. Questions (remaining time)

---

## Demo script

Rehearse this until it takes six minutes. Before starting, **sign out of
everything, open each tab in advance, and have a fallback screen recording**
in case the venue network fails.

Demo on the live site, <https://theebuhle-foundation.azurewebsites.net>, and
the `azure` build of the app (`./gradlew :app:installAzure`). The live demo
accounts use the password the group shares privately, not the README's. Never
show a password on screen: type it off-camera or use pre-signed-in browser
profiles.

**Open the site five minutes before presenting.** The free database pauses
after about an hour idle, and the first request wakes it slowly.

| # | Show | Say | Proves |
|---|---|---|---|
| 1 | Public site home, then shrink to phone width | "The client's public face. Same site on a phone, nothing scrolls sideways." | Visual design, responsiveness |
| 2 | `/impact`, then `/donate` total | "These numbers are live from the registers, not typed in." | Data flow |
| 3 | Submit the volunteer form empty, then valid | "Validation is announced to screen readers; the application lands in the staff console." | UX, accessibility, data flow |
| 3b | Staff console → **People** → add a parent, tick a learner, show the one-time code | "There is no sign-up button anywhere. Staff add a parent and link them to their child, because only the foundation knows who is whose parent. They get a code that works once." | Security, safeguarding thinking |
| 3c | App → "I have a code from the foundation" → redeem it → sign in as that parent | "The parent chooses their own password. Nobody at the foundation ever knows it." | Mobile, data flow |
| 4 | Staff console as **programme staff** → try the safeguarding queue | "Programme staff run registers and announcements, but are refused the safeguarding queue." | Security: role-based access |
| 5 | Android app as the **mentor** → file a check-in with a wellbeing concern | "A mentor flags a concern from the field." | Mobile, API |
| 6 | Staff console as the **safeguarding lead** → the concern is there | "It went straight to the designated lead, the only person who can see it. If nobody can receive a concern, the API refuses to save it and tells the mentor to phone." | Chain of Responsibility, the key design decision |
| 7 | Android app as the **parent** → Home → "Waiting for your answer" → Give permission | "A parent answers a camp form from their phone. Declining is as easy as agreeing." | Mobile, POPIA consent |
| 8 | Azure portal → the `theebuhle` database → **Query editor** (sign in with Microsoft Entra): `SELECT` the `consents` rows for one form, then run a `DELETE` on one |  "Every decision is kept. Even with direct database access the row cannot be changed; the trigger refuses." | Database integrity, security |
| 9 | Audit log in the console as admin | "Who did what, when, and it cannot be edited." | Auditability |
| 10 | GitHub Actions: a green run | "Every push runs all of this: 309 tests." | CI/CD |

If time is short, drop 2, 3c and 9. Never drop 6 or 8: they are what sets this
project apart. Steps 3b and 3c are worth keeping if the questions are likely to
be about security, because an invite-only account system is a decision most
groups will not have made.

---

## Delivery · *Communication & professionalism*

- Every member speaks, and each person presents the part they built.
- Speak about the **client**, not the technology: say "a parent can…", not "the
  endpoint returns…".
- Use real names for the client and its programmes, and seeded names for
  learners. Never show a real child's data.
- Answer "why" questions from the docs: architecture choices are written up
  in `docs/architecture.md`, security in `docs/security.md`.
- Likely questions to prepare:
  - *Why not microservices?* One small NPO, one deployment, lower cost.
  - *Why is there no sign-up page?* An account shows a child's marks,
    attendance and mentor notes. Open registration would let anybody claim to
    be a parent, so staff create every account and hand out a one-time code.
  - *How do you stop a parent seeing another child?* `AccessPolicy` checks
    guardianship on every learner read, and an integration test proves it.
  - *What happens if the connection drops?* The Android app shows an offline
    state with a retry button rather than failing silently, and nothing is
    half-written: each save is one transaction.
  - *How is a password stored?* Argon2id, per-password salt, never
    recoverable.
