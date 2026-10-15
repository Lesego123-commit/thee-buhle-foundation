# API reference

76 endpoints under `/api`. Swagger UI is served at `/swagger` in Development and
is generated from the same XML comments that document the C#, so it cannot drift
from the code.

## Conventions

- **JSON, camelCase.** Enums are serialised as their **names** (`"Concerned"`,
  not `3`), so a client reading a response needs no mapping table and a
  reordered enum cannot silently change meaning.
- **Bearer tokens.** `Authorization: Bearer <access token>`, 15-minute lifetime.
- **Errors are RFC 7807 problem details.** A validation failure carries an
  `errors` dictionary keyed by field; a refused business rule carries `title`
  and `detail`, and the detail is written for the person who will read it.
- **Rate limits** are per user where a user is known, and per address otherwise.

### Error shape

```json
{
  "title": "rule_violation",
  "status": 409,
  "detail": "We cannot process a donation without your consent to hold your details.",
  "instance": "/api/public/donations",
  "correlationId": "0HNONCFQ7O7O4:00000001"
}
```

| Status | Meaning here |
|---|---|
| 400 | The values are wrong. `errors` names the fields. |
| 401 | Not signed in, or the access token has expired. |
| 403 | Signed in, and not allowed. |
| 404 | No such thing — or none you may see. |
| 409 | A rule refused it. The `detail` says which, in plain words. |
| 429 | Rate limited. |
| 503 | A safeguarding concern could not be routed to anybody. Nothing was saved. |

---

## Public — `/api/public` (no authentication)

| | |
|---|---|
| `GET /programmes` | The four programmes |
| `GET /programmes/{slug}` | One programme |
| `GET /events` | Upcoming events, optionally `?type=` |
| `GET /events/{idOrSlug}` | One event |
| `GET /news` | Paged articles, `?category=&search=&page=&pageSize=` |
| `GET /news/{slug}` | One article, body included |
| `GET /impact` | The published impact snapshot |
| `GET /sponsorships/available` | Sponsorable learners — **reference code, grade, suburb and ambition only** |
| `GET /donations/impact-copy` | What each amount buys, so the site, the receipt and the app say the same thing |
| `POST /events/{idOrSlug}/register` | Register for an event |
| `POST /contact` | The contact form |
| `POST /volunteer` | A volunteer application |
| `POST /newsletter` | Subscribe |
| `POST /donations` | Start a donation; returns the gateway's signed field set |

Every `POST` here is rate limited and requires POPIA consent. Consent is checked
in the service rather than the controller, so the rule holds for any client.

`GET /sponsorships/available` excludes learners who already have a sponsor **in
the database query**, so the page cannot be used to work out who is sponsored.

---

## Authentication — `/api/auth`

| | |
|---|---|
| `POST /sign-in` | Email and password. Returns an access token; for a browser the refresh token is set as an HttpOnly cookie instead of being returned in the body. |
| `POST /refresh` | Rotates the pair. Reads the cookie when there is one, the body otherwise. |
| `POST /sign-out` | Revokes this refresh token. |
| `POST /sign-out-everywhere` | Revokes the whole family. |
| `POST /change-password` | Revokes every other session as a side effect. |
| `POST /redeem-code` | Anonymous. Email, one-time code and a new password: accepts an invitation or completes a password reset, then revokes every session. Rate-limited with sign-in. |
| `GET /me` | The current principal. |

A refresh token is usable **once**. Presenting an already-used one revokes the
entire family, which signs out both the thief and the genuine user — detection
rather than prevention, because the alternative is not noticing.

Sign-in answers identically for a wrong password, an unknown address and a
locked account. The API cannot be used to find out which addresses have accounts.

---

## Learner, parent and mentor

| Route | Who |
|---|---|
| `GET /api/dashboard` | Learner or parent. A parent sees their own children. |
| `GET /api/learners/{id}/progress` | The learner, their guardian, their mentor, or staff. |
| `GET /api/learning/subjects` · `/subjects/{id}/resources` · `/search` · `/continue` | Learner |
| `POST /api/learning/resources/{id}/progress` · `/download` | Learner |
| `GET /api/learning/learners/{id}/badges` · `/milestone` · `/marks` | Learner, guardian, mentor, staff |
| `GET /api/learners/{id}/forms` · `POST /api/forms/{id}/consent` · `GET /api/forms/{id}/consent/history` | Guardian |
| `GET /api/peers/suggestions` · `/requests` · `/groups` | Learner |
| `POST /api/peers/requests/{recipientId}` · `/groups/{id}/join` | Learner — **requests only**; approval is a staff action |
| `GET /api/messages` · `/notifications` · preferences · device tokens | Any signed-in user |
| `GET /api/mentor/dashboard` | Mentor |
| `POST /api/mentor/check-ins` | Mentor |

There is **no endpoint for a learner to message another learner**. Not
restricted, not moderated — it does not exist, and its absence is the control.

---

## Staff — `/api/admin`, `/api/attendance`

| Route | Who |
|---|---|
| `GET /api/attendance/sessions/recent` · `/{id}/register` | Staff |
| `POST /api/attendance/sessions/{id}/register` | Staff — bulk, all-or-nothing |
| `POST /api/attendance/sessions/{id}/entries` | Staff — a single mark |
| `GET /api/admin/learners` · `/{id}/check-ins` | Staff |
| `POST /api/admin/announcements` · `/permission-forms` | Staff |
| `POST /api/admin/mentorships` | Staff — refused if the mentor's clearance is not current |
| `GET /api/admin/pairings/pending` · `POST /pairings/{id}/decide` | Staff |
| `GET /api/admin/volunteers` · `POST /volunteers/{id}/status` | Staff |
| `GET /api/admin/contact-messages` · `POST /{id}/status` | Staff |
| `GET /api/admin/donations` · `/donations/totals` | Staff |
| `POST /api/admin/impact/refresh` | Staff |
| `GET /api/admin/audit` · `/audit/{entity}/{id}` | **Safeguarding lead or administrator** |
| `GET /api/admin/people` | Staff: accounts, waiting invitations first |
| `POST /api/admin/people/learners` · `/parents` · `/mentors` | Staff: creates the person and, if they sign in, an invite code |
| `POST /api/admin/people/staff` | **Administrator only** |
| `POST /api/admin/people/{userId}/code` | Staff: a new invite, or a password reset for an active account. **Only an administrator** may issue one for a staff-side account (403 otherwise). |

The code is in the response **once**; only its hash is stored. `redeem-code`
answers every failure (unknown email, wrong, expired, used or dead code) with the
same 409 and the same sentence, so it reveals nothing about which part was wrong.

A bulk register saves **all of it or none of it**. A half-saved register is
worse than an unsaved one, because nobody can tell which half is missing.

---

## Safeguarding — `/api/safeguarding`

| Route | Who |
|---|---|
| `GET /concerns` | **The designated safeguarding lead only** |
| `POST /concerns/{checkInId}/close` | The lead. Requires a written outcome. |
| `GET /escalation-chain` | The lead — who concerns currently route to |

Programme staff are refused all three with a 403, and the authorisation tests
assert it. The notes are not visible to the parent, the learner, or **the mentor
who wrote them** — a concern may be about an adult in the child's life, and it
has to be able to reach somebody who is not that adult.

Closing a concern **updates** the row. It is the safeguarding record and it
survives being resolved; closing takes it out of the queue and nothing else.

---

## Payments — `/api/payments`

| | |
|---|---|
| `POST /callback` | The gateway's server-to-server notification |
| `GET /return` · `GET /cancel` | Where the donor's browser comes back to |

The callback applies four checks before it believes anything: the signature, the
source address, that the amount matches the donation it claims to complete, and
whether this reference has already been completed. The fourth is what makes a
duplicate delivery a no-op rather than a double count.

**No card details reach this API.** There is no field for one in any request
shape, and no column for one in any table.
