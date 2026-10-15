# Requirements traceability

Task 1 specified this system. This maps what it committed to onto the code that
implements it, so a claim in that document can be checked rather than taken on
trust.

Where the code cites a section number in a comment, the citation is in the
source and `grep -rn "section 9.3"` will find it.

---

## Architecture and design

| Task 1 | Commitment | Where it lives |
|---|---|---|
| §9.3.11.1 | Signed, short-lived download URLs rather than public file links | `LearningController` — `POST /resources/{id}/download` returns a signed URL; `Infrastructure/Security/HmacUrlSigner.cs` |
| §9.3.11.2 | A read model that can lag without corrupting anything | `learner_summaries` and `impact_metrics`, refreshed by `ImpactService` |
| §9.3.11.4 | Non-sequential record identifiers | `Entity.Id` is `Guid.CreateVersion7()` throughout — time-ordered for index locality, not guessable |
| §9.3.12.1 | Five design patterns | See [architecture.md](architecture.md#the-five-design-patterns) |
| §9.3.12.2 | One access-control point consulted by every client | `Application/Security/AccessPolicy.cs` — the website, the console and the mobile app all reach it through the same API |

### The five patterns

| Pattern | Implementation |
|---|---|
| Repository + Unit of Work | `Domain/Repositories/`, `Infrastructure/Repositories/`, `Persistence/UnitOfWork.cs` |
| Observer | `Domain/Events/DomainEvents.cs`, `Application/Listeners/` |
| Strategy | `Application/Services/NotificationService.cs`, `Infrastructure/Notifications/NotificationChannels.cs` |
| Chain of Responsibility | `Application/Safeguarding/ConcernHandler.cs` |
| MVVM | `android/.../ui/**` — a ViewModel and a Fragment per screen |

---

## Security

| Task 1 | Commitment | Where it lives | Proven by |
|---|---|---|---|
| §9.3.13.4 | Argon2id password hashing | `Infrastructure/Security/Argon2PasswordHasher.cs` | `Argon2PasswordHasherTests` |
| §9.3.13.5 | Sanitise HTML on write, against an allow-list | `AnnouncementService`, `Infrastructure/Support/HtmlSanitiser` | The article page uses `Html.Raw` exactly once, on a value sanitised before storage |
| §9.3.13.6 | POPIA data minimisation at the point of collection | `volunteer_applications.id_number_last4`; truncated server-side | `PublicFormTests`; the form says so where it asks |
| §9.3.13.7 | Safeguarding notes reach the lead and nobody else | `AccessPolicy.CanReadFlaggedConcerns`, `SafeguardingController` | `SafeguardingFlowTests` — invisible to parent, learner and the mentor who wrote it |
| §9.3.13.8 | Authentication failures logged at a level worth alerting on | `AuthService` log messages at Warning | — |

---

## Functional requirements

| Requirement | Web | Mobile | API |
|---|---|---|---|
| Public information about the organisation and its programmes | `/`, `/programmes`, `/programmes/{slug}` | — | `GET /api/public/programmes` |
| Published impact figures, honestly derived | `/impact` | — | `GET /api/public/impact` |
| Events and registration | `/events` | — | `GET`/`POST /api/public/events` |
| News and success stories | `/news`, `/news/{slug}` | — | `GET /api/public/news` |
| Donations, including child sponsorship | `/donate` | — | `POST /api/public/donations` |
| Volunteer applications with screening | `/volunteer` | — | `POST /api/public/volunteer` |
| Contact, with safeguarding routing | `/contact` | — | `POST /api/public/contact` |
| Secure accounts for participants, parents and mentors (invite-only) | `/admin` → People; `/activate` | `SignInFragment`, `ActivateFragment` ("I have a code") | `POST /api/admin/people/*`, `POST /api/auth/redeem-code` |
| Forgotten password (staff-issued reset code) | `/admin` → People → Reset password; `/activate` | `ActivateFragment` | `POST /api/admin/people/{id}/code`, `POST /api/auth/redeem-code` |
| Learner dashboard | — | `HomeFragment` | `GET /api/dashboard` |
| Learning resources by subject and grade | — | `LearnFragment`, `SubjectFragment` | `GET /api/learning/*` |
| Progress: attendance, marks, badges | — | `ProgressFragment`, `BadgesFragment` | `GET /api/learners/{id}/progress` |
| Study buddies and peer groups, staff-approved | — | `BuddiesFragment` | `GET`/`POST /api/peers/*` |
| Announcements and notifications | — | `MessagesFragment`, `NotificationsFragment` | `GET /api/messages`, `/api/notifications` |
| Parent view of their own child | — | `HomeFragment` (parent role) | `GET /api/dashboard` |
| Parental consent for activities | — | `HomeFragment` ("Waiting for your answer" → Give permission / Decline) | `GET /api/learners/{id}/forms`, `POST /api/forms/{id}/consent` |
| Mentor's assigned learners | — | `MenteesFragment` | `GET /api/mentor/dashboard` |
| Mentor check-ins, with concern flagging | — | `CheckInFragment` | `POST /api/mentor/check-ins` |
| Attendance registers | `/admin` → Register | — | `POST /api/attendance/sessions/{id}/register` |
| Learner roll | `/admin` → Learners | — | `GET /api/admin/learners` |
| Publishing announcements | `/admin` → Announcements | — | `POST /api/admin/announcements` |
| Volunteer screening queue | `/admin` → Volunteers | — | `GET`/`POST /api/admin/volunteers` |
| Website enquiries | `/admin` → Enquiries | — | `GET`/`POST /api/admin/contact-messages` |
| Donation records and totals | `/admin` → Donations | — | `GET /api/admin/donations` |
| Safeguarding queue | `/admin` → Safeguarding | — | `GET /api/safeguarding/concerns` |
| Audit log | `/admin` → Audit log | — | `GET /api/admin/audit` |

---

## Non-functional requirements

| Requirement | How it is met | How you would check |
|---|---|---|
| Works on a slow mobile connection | Server-rendered pages, no framework bundle, one stylesheet and two small scripts | View source on any page |
| Data residency in South Africa | Azure SQL, South Africa North | `docs/deployment.md` |
| Auditability | `audit_log`, made immutable by an `INSTEAD OF` trigger; every actor column a foreign key to `users` | Try to `UPDATE audit_log` at a SQL prompt |
| Availability during deployment | Staging slot, health check, swap | `.github/workflows/deploy.yml` |
| Maintainable by a non-specialist | No front-end build step; migrations rather than hand-run SQL | `README.md` quick start |
| Accessible | WCAG 2.2 AA: semantic markup, `aria-current`, `aria-invalid` with `aria-describedby` on errors, visible focus, a skip link, 4.5:1 text contrast, 24px targets, keyboard-reachable scrolling tables; see [accessibility.md](accessibility.md) | Submit any form empty; Tab through /impact |

---

## The mobile platform changed

Task 1 and the first implementation used **React Native with Expo**. The app is
now **native Android in Kotlin**, with XML layouts and Fragments.

The reasons, recorded so the change is a decision rather than a drift:

1. **The module and the client expect native Android.** The deliverable is
   marked against Android development, and XML layouts with Fragments are what
   that brief describes.
2. **The React Native client could not reach this API.** It had been written
   against the PHP prototype's routes, base URL and response envelope, and of
   eighteen call sites checked, two would have worked had only the base URL been
   corrected. Rewriting the client was most of the work either way.
3. **MVVM is a Task 1 commitment, and Android implements it natively.**
   `ViewModel` and `StateFlow` are the platform's own version of the pattern
   rather than an approximation of it in hooks.
4. **The audience is Android.** Learners in the Vaal are on Android phones, and
   the iOS half of a cross-platform toolkit was buying nothing.

What was lost: iOS, which the client has never asked for, and the Expo
toolchain's 33 outstanding npm advisories, which went with it.

---

## Deviations from Task 1

Task 1 proposed a Firebase stack — Firestore, Cloud Functions, Firebase Auth.
The implementation is ASP.NET Core with SQL Server on Azure. The reasons, so the
change is a decision rather than a drift:

1. **The module and the client both expect .NET.** The deliverable is assessed
   as a C#/.NET project and the group's skills are there.
2. **The data is relational.** Learners, guardians, enrolments, sessions,
   attendance and consent are joins. Modelling them in a document store means
   maintaining consistency in application code, which is exactly where a
   safeguarding bug would hide.
3. **Constraints move into the database.** "Exactly one safeguarding lead" is a
   filtered unique index here. In Firestore it would be a Cloud Function that
   somebody could bypass.
4. **Residency.** Azure has a South African region; POPIA makes that worth more
   than a feature comparison.

What survived the change is every property Task 1 argued for: the signed
download URL, the lag-tolerant read model, non-sequential identifiers, one
access-control point, and the five patterns. The sections above cite where.
