# Security, POPIA and safeguarding

This system holds records about children. Most of what follows exists because of
that, not because of a checklist.

---

## Authentication

### Passwords

**Argon2id** (Konscious.Security.Cryptography), 19 MiB memory, 2 iterations,
parallelism 1, a 16-byte random salt per password and a 32-byte hash — the OWASP
minimum (OWASP, 2024; see [references.md](references.md)). Those are
the OWASP-recommended parameters for Argon2id at the time of writing, and the
hasher records them in the stored string so a future increase can re-hash on
next sign-in rather than invalidating every password at once.

Passwords are never recoverable, including by an administrator. There is no
endpoint that returns one and no log line that could contain one.

### Sessions

| | |
|---|---|
| Access token | JWT, **15 minutes**, signed HS256 |
| Refresh token | Opaque random value, **30 days**, stored as a SHA-256 hash |

The refresh token is stored hashed for the same reason a password is: a dump of
the `refresh_tokens` table must not be a set of working credentials.

Refresh tokens **rotate**. Each use revokes the presented token and issues a new
one, so a token works exactly once. Presenting one that has already been used
revokes the whole family — which signs out the thief and the genuine user
together. That is deliberate: a stolen session that is silently shared is worse
than an interrupted one.

For a **browser**, the refresh token is returned as an `HttpOnly` cookie and
never appears in the response body. The staff console therefore cannot read it,
store it, or leak it through a script.

The **Android app** asks for it in the body — it has no cookie jar worth relying
on — and keeps both tokens in `EncryptedSharedPreferences`, encrypted at rest
with a key held in the Android Keystore, hardware-backed on most devices. An
expired access token is renewed by an OkHttp `Authenticator`, which OkHttp
consults *after* a 401 and then retries the original request, so a screen whose
token expired mid-scroll reloads rather than bouncing the user to sign-in. The
refresh is synchronised: four screens loading at once produce four 401s, and
without the lock each would spend the same single-use refresh token and the API
would revoke the whole family.

### Accounts: invite-only

There is **no public sign-up**. An account here is a view of a child's marks,
attendance and mentor notes, and open registration would let anybody claim to be
somebody's parent. Staff create each person in the console's People section,
linking a parent to their children, and the person receives a **one-time code**.
They redeem it in the app ("I have a code") or at `/activate` and choose their
own password, so no member of staff ever knows it.

| Rule | Why |
|---|---|
| Only a SHA-256 hash of each code is stored | A copy of the database is not a set of working invitations |
| Codes are 10 characters from a 31-character alphabet with no 0/O/1/I/L | About 49 bits, and still readable over the phone |
| A code works **once**, expires (invite 7 days, reset 2 days) and dies after **5 wrong guesses** | Online guessing is hopeless; the redeem endpoint is also rate-limited |
| Issuing a new code revokes any older one | A code sent to the wrong number can be cancelled |
| Every refusal gets the same message | The endpoint does not reveal whether an email has an account |
| Redeeming signs out every existing session | A reset protects someone whose password was stolen |
| Programme staff cannot issue a code for a staff-side account | Otherwise they could reset the safeguarding lead's password and read the safeguarding queue |
| Suspended or deactivated accounts get no code | A reset must not undo a suspension |
| An invited account cannot sign in | Its placeholder password is random and never shown, and the status check refuses it first |

A forgotten password is a **staff-issued reset code**, not an emailed link:
anybody who learns a parent's email address could start a self-service reset,
and the foundation's email is not configured yet. When it is, codes are also
emailed automatically.

Every creation, code issue, failed guess and redemption goes to the audit log.

### Lockout

Five failed attempts locks an account for fifteen minutes. The sign-in response
is identical for a wrong password, an unknown address and a locked account, so
the API cannot be used to enumerate who has an account.

---

## Authorisation

One policy class — `Application/Security/AccessPolicy.cs` — answers every
"may this person see this?" question, and the controllers consult it. The rules
that matter:

- **A parent sees their own child and no other.** The boundary is resolved
  against the `guardian_links` row that staff created at enrolment, never
  against a claim in the token.
- **A mentor sees their assigned learners**, through the active assignment.
- **Programme staff see programme data** — and are refused the safeguarding
  queue and the audit log.
- **The designated safeguarding lead** is the only role that may read flagged
  concerns. The role is *derived* from the `is_safeguarding_lead` flag on the
  staff row rather than stored on the account, so there is one source of truth
  for who may read child-safety reports.

A parent asking for another family's child gets **409 with an identical
message** to the one for a child who does not exist. Distinguishing the two
would confirm that a particular learner is on the programme.

The whole matrix is asserted over HTTP in
`tests/TheeBuhle.IntegrationTests/AuthorisationTests.cs`, including the negative
cases, because a route with a missing attribute passes every unit test.

---

## Rate limiting

| Policy | Limit |
|---|---|
| Sign-in | 5 attempts per 5 minutes |
| Public forms | 10 submissions per hour |
| Global | Token bucket per principal, or per address when anonymous |

Applied **after** authentication, so the limiter partitions by user where one is
known. Thirty parents on a venue's shared Wi-Fi have one address between them
and would otherwise throttle each other.

---

## Input and output

- **HTML is sanitised on write.** Announcement bodies and news articles pass
  through an eleven-tag allow-list before they are stored, so the database
  cannot hold a payload at all. Sanitising on write rather than on read means a
  future template that forgets to escape, or a second client that renders the
  field differently, is not a new vulnerability. `Html.Raw(` appears exactly
  once in the entire web project, on the article page, and is grep-able for that
  reason.
- **Every query is parameterised.** EF Core does this; there is no string-built
  SQL anywhere in the project.
- **Security headers** are set by middleware on every response, including
  errors: a content-security policy, `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`
  and a `Permissions-Policy` that turns off geolocation, microphone and camera.
- **`script-src 'self'` with no `'unsafe-inline'`**, so there is no inline
  script anywhere in the site or the console. `form-action` permits only this
  origin and PayFast.
- **The `Server` header is off** at the Kestrel level. Removing it in middleware
  does nothing, because Kestrel writes it after middleware has run.

---

## POPIA

The Protection of Personal Information Act, 2013. What the system does about it,
in the order somebody auditing would ask:

### Consent is recorded, and its history is kept

`consents` is append-only, and the database enforces it with a trigger that
refuses every `UPDATE` and `DELETE`. Withdrawing permission writes a new
decision rather than editing the old one, so the full history stays reconstructible: what was
agreed, and when it changed. A table that overwrites cannot answer the second
half of that question.

Every public form that collects personal information asks for consent, refuses
without it, and **stores the answer**. That last part was a real gap: the
donation and event-registration forms asked, the API had nowhere to put the
answer, and the consent evaporated. Both tables now carry `consent_popia`, and
the services refuse the write without it.

### Data minimisation

- **Volunteers:** only the last four digits of an ID number are kept, truncated
  server-side before anything is persisted.
- **Donors:** name, email, phone and the gateway's reference. **No card
  details** — there is no field for one in any request shape and no column for
  one in any table.
- **Learners in public:** a reference code, a grade and a suburb. Never a name,
  never a photograph.

### Purpose, and saying so at the point of collection

Each form states why it is asking, next to the field. The volunteer form says
the ID number is for the SAPS clearance and that only four digits are kept. The
donation form says card details are entered on the provider's site. The contact
form says a reported concern goes to the safeguarding lead and not to the
general inbox.

The [privacy policy](../src/TheeBuhle.Api/Pages/Privacy.cshtml) describes what
the system actually does rather than covering the organisation with
generalities. Every claim in it corresponds to something in the schema, which
means it is also a test: if one of those claims stops being true, a sentence on
a published page becomes false.

### Retention

Stated publicly: learner records for five years after the programme, consent
records seven years, donation records seven years (SARS requires it for Section
18A), volunteer clearance documents seven years after the volunteer leaves,
website enquiries two years.

### Residency

Azure SQL in **South Africa North**. Data stays in the country.

### No trackers

No analytics, no advertising, no third-party cookies — and therefore no cookie
banner. The only cookie the site sets is the refresh-token cookie for a signed-in
staff member.

---

## Child safeguarding

The rules the system enforces, each corresponding to something in the code.

### Screening before assignment

A mentor cannot be assigned to a learner unless their clearance is recorded as
current. An expired clearance blocks the assignment outright
(`MentorshipService`), and the database half of the rule — a cleared mentor must
have an expiry date — is a check constraint.

### A concern reaches somebody, or nothing is saved

A flagged concern routes along a chain: mentor lead, programme manager,
designated safeguarding lead. Anything touching a child's wellbeing, home or
safety goes straight to the lead.

If **no link can take it**, the API refuses to save the check-in at all, answers
503, and tells the mentor to phone instead. A concern that looked filed and
reached nobody would be the worst failure this system could have. The
integration test asserts the 503, that the check-in count is unchanged, and that
two critical log lines were written.

### A concern is invisible to almost everybody

Not to the parent, not to the learner, and **not to the mentor who raised it**.
Only the designated safeguarding lead can read it. A concern may be about an
adult in the child's life, and it has to be able to reach somebody who is not
that adult.

Verified over HTTP: the note text appears in exactly one table, and zero times
in the audit log or in any notification.

### There is exactly one lead

Enforced by a filtered unique index, so a second cannot be created by a bug, a
seeder or an administrator. There is never any doubt about who is responsible.

### Learners cannot message each other

There is no endpoint. A study buddy can be **requested** by a learner and only
**approved** by a mentor or staff member; groups are created and joined by
approval. Learners see each other as first name, surname initial, grade and
suburb — enough to recognise somebody from a Saturday session, not enough to
identify a stranger's child.

---

## Supply chain

Three GitHub Actions run on every push and weekly:

- **CodeQL** over the C#.
- **`dotnet list package --vulnerable`**, failing on any known advisory.
- **Gitleaks**, over the full history rather than the diff.

CodeQL scans three languages: C# for the API, JavaScript for the site's own
scripts, and Kotlin for the Android app.

There is no npm advisory job any more, and its absence is worth recording. The
React Native app this project used to carry brought **33 outstanding high and
critical advisories**, every one of them in the build toolchain — Metro, the
Expo CLI, tar, cacache, postcss — none of which ran on a learner's phone and
none of which could be fixed without a major Expo upgrade. It needed a script
that compared the current advisories against a reviewed list and failed on the
difference, purely to keep a permanently red check from being ignored.

The Android app has no npm dependency tree at all. Its dependencies are pinned
in `android/gradle/libs.versions.toml`, and the 33 advisories went with the app
that carried them.

---

## What is not done

- **No penetration test.** Nothing here has been attacked by somebody trying.
- **No two-factor authentication.** Worth adding before the safeguarding lead's
  account holds a year of concerns; it is a single account and a single screen.
- **Secrets are GitHub repository secrets**, not a key vault. Adequate for a
  student project; Azure Key Vault with a managed identity is the next step, and
  the deployment already authenticates with a federated credential rather than a
  stored password.
