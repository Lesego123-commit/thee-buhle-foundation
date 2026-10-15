# Getting in

For group members and anyone marking this project. Everything here is the
**live** system. To run it on your own machine instead, see the README.

---

## The website

<https://www.theebuhlefoundation.co.za> — the foundation's own domain, with
HTTPS. **Use the `www.` address for now.** The plain
<https://theebuhlefoundation.co.za> serves the same site, but a network that
looked the domain up before it moved may still show Afrihost's old placeholder
page until that saved copy expires.

<https://theebuhle-foundation.azurewebsites.net> is the same site on its Azure
address, and keeps working whatever happens to the domain.

Nothing on the public pages needs an account: programmes, impact, events, news,
donations, the volunteer form and the contact form are all open.

**The database sleeps.** It is on Azure SQL's free plan and pauses after an
hour with nobody on the site. The first page load after that can take up to a
minute, or fail once and work on a retry. **Open the site about five minutes
before a demo.**

---

## The staff console

<https://www.theebuhlefoundation.co.za/admin>

| Account | Role | What it shows |
|---|---|---|
| Lesego's own account | Administrator | Everything, including the audit log |
| `admin@theebuhlefoundation.co.za` | Administrator (demo) | Everything |
| `safeguarding@theebuhlefoundation.co.za` | Safeguarding lead | The safeguarding queue — the one thing nobody else can see |
| `programmes@theebuhlefoundation.co.za` | Programme staff | Registers, learners, announcements, volunteers; **refused** the safeguarding queue and the audit log |

Signing in as programme staff, then as the safeguarding lead, is the quickest
way to see the access control the system is built around.

---

## The mobile app

The app is for **learners, parents and mentors**. Staff use the console
instead.

### Installing it

With Android Studio and the repository open, or from a terminal in
`part 2/android`:

```bash
./gradlew :app:installAzure
```

That installs the build pointed at the **live** site, which is the one to use
for a demo. `installDebug` points at an API running on your own machine, and a
release build points at the domain.

### Accounts

| Email | Role | What you see |
|---|---|---|
| `nomvula.mahlangu@example.co.za` | Parent | Her children's attendance, marks and badges, and permission forms waiting for an answer |
| `thandi.mokoena@example.co.za` | Mentor | Assigned learners, and check-ins where a concern can be flagged |
| `lebo.mahlangu@learner.example` | Learner, Grade 10 | Learn, Progress, Badges, Buddies |
| `karabo.nkosi@learner.example` | Learner, Grade 11 | The same, with different data |
| `tumi.molefe@learner.example` | Learner, Grade 12 | The same |

Three of the six demonstration learners have logins. The younger ones do not,
on purpose: their parents follow their progress instead.

---

## Passwords

**Ask Lesego.** The live accounts share one demonstration password, chosen when
the demonstration data was loaded and deliberately never written down here or
anywhere else in this repository.

The password printed in the README works **only** against an API running on
your own machine. It is in the repository, so it must never be the password on
a public site.

Keep the demonstration password to the group. It opens a staff console.

---

## If you are locked out

- **Five wrong attempts** locks an account for fifteen minutes. Wait, rather
  than trying again.
- **Forgot the password:** an administrator opens the console's **People**
  section, finds the account and clicks **Reset password**. That produces a
  one-time code.
- **Use a code:** in the app, tap **"I have a code from the foundation"** on
  the sign-in screen; on the web, go to
  <https://www.theebuhlefoundation.co.za/activate>. Enter the email address, the
  code and a new password.

There is no public sign-up anywhere, and that is deliberate: an account here
shows a child's records, so the foundation decides who gets one. See
[security.md](security.md) for the rules the codes follow.

---

## Ground rules while testing

- **The live data is invented**, loaded for the presentation. Treat it as if it
  were real anyway: it is the habit that matters once the foundation puts real
  children in there.
- **Do not put real children's details into the live site.**
- **The demonstration password is not for slides, screenshots or a group chat.**
  Type it off-camera during the presentation.
- **Found something broken?** Say which account you used, which screen, and
  what you expected. The audit log records what happened, so a time helps.
