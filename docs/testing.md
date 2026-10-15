# Testing

**309 automated tests**: 207 unit, 84 integration, 18 Android.

```bash
dotnet test tests/TheeBuhle.UnitTests
dotnet test tests/TheeBuhle.IntegrationTests   # needs SQL Server
cd android && ./gradlew :app:testDebugUnitTest
```

---

## Unit tests — 207

No database, no HTTP, no mocking framework. The repositories are interfaces in
the Domain, so the fakes in `Fakes/FakeRepositories.cs` are ordinary classes
holding lists. A fake that is a real class is readable when a test fails, which
a mock's expectation chain is not.

| File | What it pins |
|---|---|
| `Domain/SupportAreaVocabularyTests.cs` | The concern vocabulary, structurally — including that no area can be urgent and unrecognised at once |
| `Security/AccessPolicyTests.cs` | The whole authorisation matrix, per role, per relationship |
| `Security/Argon2PasswordHasherTests.cs` | Hashing, verification, and that outdated parameters are detected |
| `Security/HmacUrlSignerTests.cs` | Signature generation and tamper rejection |
| `Safeguarding/EscalationChainTests.cs` | Each link, and the terminal case where nobody can take a concern |
| `Services/AttendanceServiceTests.cs` | Marks, amendments, enrolment checks, all-or-nothing capture |
| `Services/BadgeEvaluatorTests.cs` | Award rules, including that an excused absence breaks a streak |
| `Services/ConsentServiceTests.cs` | Append-only decisions and the sequence ordering |
| `Services/DonationServiceTests.cs` | Amounts, sponsorship, the four webhook checks, consent |
| `Services/MentorshipServiceTests.cs` | Clearance before assignment, check-ins, escalation |
| `Events/DomainEventDispatcherTests.cs` | Advisory versus non-advisory listener failure |

The vocabulary tests deserve a note. They do not test behaviour — they assert
that `SupportAreas.All` is the union of the three classification sets and that
no area is missing from it. That is a structural test for a structural bug: when
the list lived in three files, `"abuse"` was urgent in one and absent from
another, so a concern tagged with it was dropped before anything could escalate
it. The test makes the class of bug impossible rather than the instance.

---

## Integration tests — 84

Real HTTP through `WebApplicationFactory<Program>`, against a **real SQL
Server**. Each fixture creates its own database from a GUID, runs the real
migrations and the real seeder, and drops it afterwards.

Not an in-memory provider. Filtered unique indexes, check constraints and the
retrying execution strategy only behave like themselves on the real engine, and
those are exactly the things worth testing.

| File | What it covers |
|---|---|
| `AuthorisationTests.cs` | The matrix over HTTP: anonymous refusals, parent boundaries, staff versus safeguarding lead, and the control case that public routes really are public |
| `SafeguardingFlowTests.cs` | A concern from a mentor's check-in through escalation to the lead's queue — including what nobody can see |
| `RateLimitingTests.cs` | The limiter, from a single address |
| `WebsiteTests.cs` | Every page renders; 404s carry both a status and a body; the donate page has no card field |
| `ConsoleTests.cs` | The staff console shell, that it ships no learner data, and that no page carries an inline script |
| `PublicFormTests.cs` | Consent is required and recorded; validation messages are written for people |

Two of these suites exist because of bugs that every other kind of test missed.

`ConsoleTests.A_page_carries_no_inline_script` was written after the site's own
content-security policy blocked the inline script that set the API's base URL.
Nothing errored server-side, every page rendered, the test suite was green — and
not one form on the website worked, because the client had fallen back to a
default path this build has never served. A blocked inline script is silent, so
the test is about the markup rather than about behaviour.

`PublicFormTests.A_public_form_never_answers_with_framework_wording` posts an
empty body to three endpoints and asserts that no message contains "The field "
or "is required". An attribute added later without a message of its own fails
the build rather than reaching a parent.

---

## Android tests — 18

JVM unit tests, no device needed.

| File | What it pins |
|---|---|
| `ApiErrorTest` | The mapping from RFC 7807 problem details onto the one error type the screens handle — including that a validation message reaches the field it is about, and that `ApiError` is an `IOException` |
| `TokenRefreshTest` | Token rotation against a real `MockWebServer`: a 401 refreshes and retries, a refused refresh ends the session instead of looping, and neither sign-in nor the refresh call carries a stale bearer |
| `WireFormatTest` | Real response bodies, copied from the running API, decoded into the DTOs |

Three of these deserve a note.

`ApiError is an IOException` looks like a triviality and is not. OkHttp permits
an interceptor to throw only an `IOException`; anything else becomes a fatal
exception on the dispatcher thread. The first version extended `Exception`, and
the app **crashed on its first unauthorised request** — the error was correct
and it took the process down with it.

`TokenRefreshTest` uses MockWebServer rather than a mocked OkHttp, because the
behaviour under test is OkHttp's own — that an `Authenticator` is consulted
after a 401 and the original request is then retried. Mocking the library would
assert only that the mock does what the test says.

`WireFormatTest` pins payloads copied from the running API rather than written
to suit the DTOs. That is the test the previous mobile client did not have: it
was written against a different API's shapes, compiled cleanly, passed its own
tests against a mocked transport, and could not read a single response. The
badge test is the concrete case — the catalogue endpoint sends `awardedAt: null`
for a badge not yet earned, and a DTO that required it parsed the earned ones
and threw on the first unearned one.

The screens are not tested. There is no Espresso suite and no screenshot tests;
the eleven screens were verified by driving the app on an emulator against the
running API.

---

## What is not tested

Stated so nobody assumes otherwise:

- **No UI automation.** The website and console were verified by driving them in
  a browser by hand; there is no Playwright suite.
- **No load testing.** The rate limiter is tested for correctness, not the
  application for throughput.
- **Push notification delivery.** The API's queueing is tested. The Android app
  has no Firebase Cloud Messaging integration at all, so there is nothing to
  test on the client side yet.
- **The Android UI.** No Espresso, no screenshot tests. Eleven screens were
  checked by hand on an emulator against the live API — which found four real
  defects that compiling did not: a crash on the first 401, a start destination
  that loaded Home before sign-in, a parent seeing an empty Learn tab, and a
  parent being shown a Buddies tab the API refuses them.
- **Email delivery at volume.** MailKit is tested against a local server, not
  against Afrihost's sending limits.
- **The payment gateway's own behaviour.** The callback's four checks are tested
  with constructed payloads. PayFast's sandbox has been used by hand; it is not
  in the automated suite, because a test that depends on somebody else's server
  fails for reasons that have nothing to do with this code.

---

## Two tests that were wrong

Worth recording, because both times the instinct was to change the code.

**A vacant mentor-lead role.** A test asserted that a mentoring concern with no
mentor lead in post fell through to the safeguarding lead. It should fall to the
programme manager — sending a confidence concern to the child-protection queue
is exactly how a queue stops being read. The test was wrong; the chain was
right.

**A substring collision.** A test asserted that a learner's name never appears
in the audit log, and it failed. The fake's default surname is `"Learner"`, and
the audit context key is `"learner_reference"`. The assertion was matching its
own scaffolding.
