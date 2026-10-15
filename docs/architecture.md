# Architecture

## The shape of it

A layered, modular monolith. One deployable application, four projects, and
references that point in one direction only:

```
TheeBuhle.Api  ──►  TheeBuhle.Infrastructure  ──►  TheeBuhle.Application  ──►  TheeBuhle.Domain
     │                                                       │                        ▲
     └───────────────────────────────────────────────────────┴────────────────────────┘
                         (Api and Infrastructure both reference Domain)
```

**The Domain references nothing.** Not EF Core, not ASP.NET Core, not a logging
package. That is the constraint the whole structure exists to protect: a rule
about who may read a safeguarding concern is a sentence about the organisation,
not about a database, and it should be testable without one. The 207 unit tests
run in about two seconds because of it.

### Why a monolith

A microservice per bounded context would be the fashionable answer and the wrong
one here. The client is a four-person NPO. The system has to be deployable by a
student team, maintainable by whoever comes after them, and cheap enough to run
on a single App Service instance. Splitting it would buy independent scaling
that nobody needs and cost operational complexity that nobody can absorb.

The layering is what a split would need anyway. If the foundation ever grows to
the point where the learning-resources module should be its own service, it can
be lifted out along a seam that already exists.

---

## What each layer holds

### Domain — `src/TheeBuhle.Domain`

15 files, ~3 700 lines. Entities, enums, repository *interfaces*, domain events,
and the vocabulary the rest of the system speaks.

Two things here are worth pointing at:

- **`Common/SupportAreas.cs`** — the single vocabulary of what a mentor can flag
  a concern about. The three classification sets (urgent, programme, pastoral)
  are declared, and the "recognised" set is *derived* from their union rather
  than written out separately. That derivation is the fix for a real defect:
  when the list was maintained in three places, `"abuse"` was urgent in one of
  them and missing from another, so a concern tagged with it was silently
  dropped before anything could escalate it.

- **`Repositories/`** — interfaces only. The Application layer depends on
  `ILearnerRepository`, never on a `DbContext`, which is what makes the service
  tests possible with hand-written fakes instead of a mocking framework.

### Application — `src/TheeBuhle.Application`

20 files, ~4 300 lines. The services that carry the rules, the access policy,
the domain-event listeners, and the safeguarding escalation chain.

Every service takes its dependencies through the constructor and none of them
knows what a controller is. `AttendanceService` does not know whether a register
arrived from the staff console or the mobile app, which is why the same rule
protects both.

### Infrastructure — `src/TheeBuhle.Infrastructure`

30 files, ~8 900 lines (plus generated migrations). EF Core configuration,
repository implementations, the mail sender, the payment gateway, the
notification channels, the password hasher.

This is the only layer that knows SQL exists.

### Api — `src/TheeBuhle.Api`

35 files, ~5 100 lines. Controllers, middleware, Razor Pages, static assets.

Controllers are deliberately thin — bind, call a service, map the result to a
status code. Where a controller started making a decision it should not (the
safeguarding contact routing once lived in one), that decision was moved into
the service, because a rule in a controller only applies to the clients that go
through that controller.

---

## The five design patterns

Task 1 section 9.3.12.1 committed to five patterns. Each is used because it
solved a problem, and each is pointed at here so a marker does not have to hunt.

### 1. Repository + Unit of Work

**Where:** `Domain/Repositories/*.cs` (interfaces),
`Infrastructure/Repositories/*.cs` (implementations),
`Infrastructure/Persistence/UnitOfWork.cs`.

The repositories hide EF Core from the rules. The unit of work makes a
multi-table write atomic — publishing an announcement writes the announcement,
its delivery rows and a notification per recipient, and either all of that
happens or none of it does.

One detail worth knowing, because it was a production-grade bug before it was a
feature: the connection is configured with `EnableRetryOnFailure`, and a
retrying execution strategy **refuses** a hand-started transaction. Every
transaction therefore runs inside `CreateExecutionStrategy().ExecuteAsync(...)`.
Without that wrapper every transactional write in the system throws on Azure
SQL, where transient faults are normal.

### 2. Observer — domain events

**Where:** `Domain/Events/DomainEvents.cs`, `Application/Listeners/*.cs`,
`Infrastructure/Events/`.

Marking a register raises `AttendanceRecorded`. Three listeners react: badges
are evaluated, the parent is notified, and the learner's summary is refreshed.
The attendance service does not know any of them exist.

The distinction that matters is **advisory versus not**. `NotifyParentOfAttendance`
is advisory: if the email fails, the register is still saved, because a failed
notification must not lose a register. `AwardBadgesOnAttendance` is *not*
advisory — a badge is part of the learner's record, so a failure there fails the
write. `EscalateFlaggedConcern` is emphatically not advisory, for the reason in
the safeguarding section below.

### 3. Strategy — notification channels

**Where:** `Application/Services/NotificationService.cs`,
`Infrastructure/Notifications/NotificationChannels.cs`.

Email, SMS and push are interchangeable implementations behind one interface.
Each recipient has a preference, and the service picks the channel from the
preference rather than branching on it. Adding WhatsApp — which the client asks
about regularly, because it is what the Vaal actually uses — is a new class and
a new enum member, not an edit to the sending logic.

### 4. Chain of Responsibility — safeguarding escalation

**Where:** `Application/Safeguarding/ConcernHandler.cs`,
`Infrastructure/Safeguarding/`.

A flagged concern passes along a chain: mentor lead → programme manager →
designated safeguarding lead. Each link decides whether the concern is its
business; anything touching a child's wellbeing, home or safety goes straight to
the end of the chain.

The important property is the **terminal handler**. If no link accepts a
concern, the chain does not quietly return — the API refuses to save the
check-in at all, answers 503, and tells the mentor to phone instead. A concern
that looked filed but reached nobody is the worst failure this system could
have, so it was made impossible rather than unlikely. The integration suite
asserts it: 503, the check-in count unchanged, and two critical log lines.

### 5. MVVM — Android

**Where:** `android/app/src/main/java/za/co/theebuhlefoundation/ui/**/` — a
`ViewModel` and a `Fragment` per screen, with
`data/repository/Repositories.kt` as the model.

Android's own architecture, used as intended:

- **View:** a `Fragment` and its XML layout, bound with view binding. It reads
  no repository and holds no state of its own.
- **ViewModel:** holds a `StateFlow<UiState<T>>` and nothing that knows what a
  `View` is. A fragment collects it inside `repeatOnLifecycle(STARTED)`, so a
  response arriving while the user is in another app does not touch views that
  are no longer there.
- **Model:** the repositories, which map the API onto types the screens use and
  turn every transport failure into one `ApiError`.

`UiState` is a sealed interface — `Loading`, `Ready`, `Failed`, `Empty` — so a
state that is both loading and failed cannot be expressed, and the `when` in a
fragment is exhaustive. Adding a state is a compile error at every screen that
has not handled it.

Dependencies are handed in rather than fetched: `ContainerViewModelFactory`
passes the `AppContainer` to each view model, which is what lets a view model
be exercised without an Android device.

---

## Request pipeline

Order matters, and these are in this sequence for specific reasons
(`src/TheeBuhle.Api/Program.cs`):

1. **Exception handling** — first, so it wraps everything after it. A handler
   registered later cannot catch what an earlier component throws.
2. **Security headers** — early, so they are set even on a response produced by
   something that fails later, including a 500 and a 429.
3. **HSTS and HTTPS redirection** — outside Development only.
4. **Status-code pages**, scoped away from `/api` — re-executes a non-API 404
   through `/not-found`, keeping both the status code and the URL the visitor
   typed. An API 404 stays JSON, because its caller is parsing JSON.
5. **Static files**, then **routing**. Routing is registered *explicitly*: left
   to itself `WebApplication` inserts it at the very front, ahead of the
   status-code branch, and the re-executed request would then match nothing and
   answer 404 with an empty body.
6. **CORS**, named origins only.
7. **Authentication**, then the **current-user rebuild** — in that order,
   because the rebuild reads the subject claim authentication has just
   validated.
8. **Authorisation**.
9. **Rate limiting**, *after* the principal is known, so the limiter partitions
   per user rather than per address. Thirty parents on one venue's Wi-Fi share
   an address and would otherwise throttle each other.
10. **Endpoints**, then the fallback.

---

## The front end

No build step. No framework. Two plain scripts —
`wwwroot/assets/js/api.js` (the typed API client) and `site.js` (form wiring) —
plus one stylesheet.

That is a deliberate constraint for a client who may have to pay somebody to
change a phone number on the contact page in three years. A React build that
nobody in the organisation can run is a liability disguised as a modern stack.

The pages themselves are **server-rendered Razor Pages**. A client-rendered
homepage on a slow Vaal mobile connection shows a blank page and then a jump,
and a crawler or a WhatsApp link preview sees nothing at all — which for an NPO
nobody is searching for by name yet is most of what makes the site findable.

One consequence worth recording: the site sends `script-src 'self'` with no
`'unsafe-inline'`, so **there is no inline script anywhere**. The API base URL
reaches `api.js` through a `data-api-base` attribute on its own script tag. An
earlier version set it with an inline assignment, the browser refused to run it,
the client fell back to a default from the PHP prototype, and every form on the
site posted to a URL that did not exist — while every page still rendered
perfectly. There is now a test that fails if an inline script reappears.

---

## Where the work happened

The system was built API-first, then mobile, then the website and the staff
console. Each client exercises the same authorisation rules independently, which
is how several of them were found to be wrong: a rule that passes because one
client happens to ask nicely fails the moment a second client asks differently.
