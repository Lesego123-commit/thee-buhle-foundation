# Database

**SQL Server 2022** in development, **Azure SQL** in production, reached through
**EF Core 10**. 44 tables, 64 foreign keys, 85 indexes besides the primary keys
(24 of them unique, 7 filtered), 14 check constraints and 2 triggers, all created
by migrations — there is no hand-run SQL script
anywhere in this project, and the schema in production is whatever the
migrations say it is.

The diagrams are in [erd.md](erd.md), generated from the live schema.

## The tables

| Area | Tables |
|---|---|
| People | `users`, `learners`, `parent_guardians`, `guardian_links`, `mentors`, `staff` |
| Access | `refresh_tokens`, `audit_log` |
| Programmes | `programmes`, `enrolments`, `sessions`, `venues`, `attendance` |
| Mentorship | `mentor_assignments`, `check_ins`, `compliance_documents` |
| Learning | `subjects`, `resources`, `resource_progress`, `assessment_results`, `badges`, `learner_badges`, `learner_summaries` |
| Peers | `peer_groups`, `group_memberships`, `buddy_requests` |
| Consent | `permission_forms`, `consents` |
| Communication | `announcements`, `message_recipients`, `notifications`, `notification_preferences`, `device_tokens` |
| Giving | `donors`, `donations`, `sponsorships` |
| Public site | `news_posts`, `events`, `event_registrations`, `contact_messages`, `volunteer_applications`, `newsletter_subscribers`, `impact_metrics` |

Column names are `snake_case`, applied by a convention rather than by attributes
on every property (`Infrastructure/Persistence/ModelConventions.cs`).

---

## What the schema refuses to store

The interesting part of a schema is not what it holds but what it will not
accept. These are enforced in the database, so a bug in the application cannot
write past them.

### Filtered unique indexes

SQL Server allows a partial uniqueness rule, which is the right tool for
"unique, but only while this is true".

| Index | Rule |
|---|---|
| `uk_staff_single_lead` on `staff` where `is_safeguarding_lead = 1` | **There is exactly one designated safeguarding lead.** Not a policy, a constraint. A second one cannot be created by a bug, a seeder or an administrator. |
| `uk_assignment_active` on `mentor_assignments` where `status = 'Active'` | A learner has one active mentor at a time. Past assignments stay as history. |
| `uk_sponsorship_active` on `sponsorships` where `end_date IS NULL` | A learner has one current sponsor, and previous sponsorships remain on the record. |
| on `users` where `user_id IS NOT NULL` | One profile row per user per role — which is what makes "a person is a parent or a mentor, not both" true rather than hoped for. |

The other three filtered indexes are for reading rather than for rules: open
concerns (`concern_flagged = 1 AND closed_at IS NULL`), unissued Section 18A
certificates, and unread notifications. Each is the exact row set a screen or a
nightly job asks for, and filtering keeps the index small.

### Check constraints

| Constraint | Rule |
|---|---|
| `ck_learners_grade` | `grade BETWEEN 1 AND 12` |
| `ck_programme_grades` | `max_grade >= min_grade` |
| `ck_session_window` | a session ends after it starts |
| `ck_event_window` | so does an event, where an end is given |
| `ck_sp_window` | a sponsorship ends after it starts, or has not ended |
| `ck_ar_mark` | a mark is between 0 and 100 |
| `ck_rp_percent` | progress through a resource is between 0 and 100 |
| `ck_donation_amount` | a donation is more than zero |
| `ck_er_party` | a booking is for 1 to 20 people |
| `ck_buddy_not_self` | **a learner cannot request themselves as a study buddy** |
| `ck_mentor_clearance` | a cleared mentor has a clearance expiry date |
| `ck_pf_target` | a permission form belongs to exactly one session or one event — never both, never neither |

`ck_mentor_clearance` is deliberately only half the rule. The database can
insist that a cleared mentor has an expiry date; it cannot sensibly insist the
date is in the future, because that would make a row invalid as time passes
rather than when it is written. The other half — refusing to assign an
uncleared or expired mentor — lives in `MentorshipService`.

---

## Three tables that carry a policy

### `consents` — append-only

A consent decision is never updated and never deleted — and the **database
enforces it**: an `INSTEAD OF UPDATE, DELETE` trigger refuses both, with a
message saying to record a new decision instead. Withdrawing permission writes a
**new row** with a higher `sequence`, and the current answer is the highest
sequence for that form and that guardian.

The foreign key to `permission_forms` is `NO ACTION` on delete. It used to
cascade, which meant deleting a form deleted every parent's decision on it —
exactly the history this table exists to keep.

That is a POPIA requirement and it is also the only honest way to answer the
question a parent may eventually ask: *what did I agree to, and when did I
change my mind?* A table that overwrites can answer the first half of that
question and not the second.

`sequence` is monotonic per form so the order survives two decisions recorded in
the same second, which a timestamp alone would not guarantee.

### `audit_log` — immutable

Every change to a learner record, a consent decision, an account or a donation
is written here with the actor, the entity, the before and after, the IP address
and the time. Nothing in the application updates or deletes a row in it, and
**the database refuses to**: an `INSTEAD OF UPDATE, DELETE` trigger rejects both,
for any login, including an administrator at a SQL prompt.

The PHP prototype had these triggers. The .NET translation dropped them while
the staff console went on telling people the database refused the change — and
for a while that sentence was only a hope. Migration `EnforceRecordIntegrity`
restored them.

Enum values are written as **names**, not integers. An audit trail full of
`{"wellbeing":3}` is a record nobody can read in two years without the source
code of the version that wrote it.

### `volunteer_applications` — `id_number_last4`

The column is four characters wide. A full South African ID number is needed to
submit the SAPS clearance application and is not needed afterwards, so the API
truncates it **server-side, before anything is persisted** — a client that posts
a full number still results in four digits in the database.

That is POPIA data minimisation expressed as a column type, and the volunteer
form says so at the point of asking rather than in a policy nobody reads.

---

## Things that are computed, not stored

`learner_summaries` and `impact_metrics` hold figures derived from the registers
and results, refreshed by a service rather than written by hand.

The reason is stated on the public Impact page: the figures come from a nightly
snapshot rather than being calculated as the page loads, so a register still
being captured on a Saturday morning can never appear to the public as a drop in
attendance.

Two calculation decisions are worth recording because they make the numbers look
*worse* and are correct anyway:

- **Mark improvement is by term**, comparing a learner's first and most recent
  school-issued mark — not their best and worst. Best-minus-worst can only ever
  produce a positive number, which is a marketing metric rather than a
  measurement. By term, a subject that has gone backwards shows a negative, and
  one currently does.
- **An excused absence is still an absence** in the attendance rate. The kind
  word is recorded; the figure is not softened by it.

---

## Migrations

```bash
dotnet ef migrations add <Name> --project src/TheeBuhle.Infrastructure --startup-project src/TheeBuhle.Api
dotnet ef migrations script --idempotent --project src/TheeBuhle.Infrastructure --startup-project src/TheeBuhle.Api
```

In Development the application migrates and seeds on startup. In production the
**pipeline** applies the migration as an idempotent script before the new code
is deployed, and keeps the script as a build artefact — so what ran against the
production database is recoverable after the fact.

CI runs `dotnet ef migrations has-pending-model-changes` and fails the build if
the model and the migrations have drifted. That check exists because the failure
it catches is silent: the code compiles, the tests pass against a database
someone created by hand, and the first deployment to a fresh database is the one
that breaks.

### Current migrations

| Migration | What it did |
|---|---|
| `InitialSchema` | The 43 tables, the indexes and the constraints |
| `RecordPublicFormConsent` | Added `consent_popia` to `donations` and `event_registrations` — the two public forms that asked for consent and had nowhere to keep the answer |
| `EnforceRecordIntegrity` | Foreign keys on the thirteen "who did this" columns; consent history no longer cascades away with its form; the audit log and consents made immutable by trigger |
| `AddAccountCodes` | The `account_codes` table for invitations and password resets: a SHA-256 hash of each one-time code (never the code), its expiry, use and failed-attempt count, and who issued it. Additive only. |

`RecordPublicFormConsent` defaults both columns to `false` and does **not**
backfill. Rows written before it were created when the answer was not kept, and
marking them `true` would record a consent nobody can show was given.

`EnforceRecordIntegrity` repairs before it constrains. Thirteen actor columns had
no foreign key, and the development seeder had written an invented identifier
into every attendance mark and school result — seventy register entries
"recorded by" a user who never existed, on a site whose Impact page says every
register entry records who captured it. The migration sets a dangling
*optional* reference to NULL, which is the honest value for "unknown", and
attributes a dangling *required* one to the earliest administrator, because the
column cannot be empty. If there is no administrator it stops rather than
invent one. On a production database the repair touches nothing: the seeder
that wrote those rows runs in development only.
