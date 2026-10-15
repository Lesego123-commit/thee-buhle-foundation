# References

Harvard (IIE) style. This list covers the **code in this repository**: the
legislation and standards it implements, the documentation consulted while
building it, and the third-party software it depends on. Task 1's own
documentation carries its own reference list.

**No code in this repository is copied from a tutorial, a textbook or another
project.** Where an outside source decided something specific — a parameter, a
rule, the wording on a tax certificate — the file says so in a comment and cites
the source, and the full entry is below.

---

## 1. Legislation

Republic of South Africa. 1962. *Income Tax Act 58 of 1962*. Pretoria:
Government Printer.
→ Section 18A governs the tax certificates issued for donations.
Implemented in `src/TheeBuhle.Application/Listeners/IssueSection18ACertificate.cs`.

Republic of South Africa. 2013. *Protection of Personal Information Act 4 of
2013 (POPIA)*. Pretoria: Government Printer.
→ Consent, data minimisation, purpose statements and retention. Implemented
across `src/TheeBuhle.Api/Controllers/ConsentController.cs`, the append-only
`consents` table and the immutable `audit_log` (see `docs/security.md`).

Republic of South Africa. 2005. *Children's Act 38 of 2005*. Pretoria:
Government Printer.
→ Screening against the National Child Protection Register before anyone works
with children; enforced in `MentorshipService` and the volunteer flow.

---

## 2. Standards and guidelines

OWASP. 2024. *Password Storage Cheat Sheet*. [Online]. Available at:
<https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html>
[Accessed 20 September 2026].
→ The Argon2id parameters in
`src/TheeBuhle.Infrastructure/Security/Argon2PasswordHasher.cs` are its stated
minimum: 19 MiB of memory, two iterations, one degree of parallelism.

Biryukov, A., Dinu, D., Khovratovich, D. and Josefsson, S. 2021. *RFC 9106:
Argon2 Memory-Hard Function for Password Hashing and Proof-of-Work
Applications*. Internet Engineering Task Force. [Online]. Available at:
<https://www.rfc-editor.org/rfc/rfc9106> [Accessed 20 September 2026].

Jones, M., Bradley, J. and Sakimura, N. 2015. *RFC 7519: JSON Web Token (JWT)*.
Internet Engineering Task Force. [Online]. Available at:
<https://www.rfc-editor.org/rfc/rfc7519> [Accessed 20 September 2026].
→ The access-token format issued by
`src/TheeBuhle.Infrastructure/Security/JwtTokenService.cs`.

World Wide Web Consortium (W3C). 2023. *Web Content Accessibility Guidelines
(WCAG) 2.2*. W3C Recommendation, 5 October 2023. [Online]. Available at:
<https://www.w3.org/TR/WCAG22/> [Accessed 21 September 2026].
→ The audit and fixes recorded in `docs/accessibility.md`: 4.5:1 text contrast
(1.4.3), 24px targets (2.5.8), reflow (1.4.10), heading order (1.3.1).

---

## 3. Technical documentation consulted

Microsoft. 2026. *ASP.NET Core documentation*. [Online]. Available at:
<https://learn.microsoft.com/aspnet/core> [Accessed September 2026].

Microsoft. 2026. *Entity Framework Core documentation*. [Online]. Available at:
<https://learn.microsoft.com/ef/core> [Accessed September 2026].
→ Migrations, the fluent model configuration, and the `HasTrigger` declaration
the SQL Server provider needs when a table carries a trigger.

Microsoft. 2026. *Azure App Service and Azure SQL Database documentation*.
[Online]. Available at: <https://learn.microsoft.com/azure> [Accessed September
2026].
→ Managed identity, the serverless free offer and its auto-pause behaviour, and
managed TLS certificates; used throughout `docs/deployment.md`.

Google. 2026. *Android Developers documentation*. [Online]. Available at:
<https://developer.android.com> [Accessed September 2026].
→ Fragments, Navigation, ViewModel and `EncryptedSharedPreferences`.

Google. 2026. *Material Design 3*. [Online]. Available at:
<https://m3.material.io> [Accessed September 2026].
→ The Android component styles in `android/app/src/main/res/values/themes.xml`.

Square. 2024. *Retrofit* and *OkHttp* documentation. [Online]. Available at:
<https://square.github.io/retrofit> and <https://square.github.io/okhttp>
[Accessed September 2026].
→ The Android app's API client, its token refresh and its timeouts.

JetBrains. 2024. *kotlinx.serialization documentation*. [Online]. Available at:
<https://kotlinlang.org/docs/serialization.html> [Accessed September 2026].
→ How the app reads the API's JSON.

xUnit.net. 2024. *xUnit.net documentation*. [Online]. Available at:
<https://xunit.net/docs/getting-started/v3/getting-started> [Accessed September
2026].
→ The structure of the 235 unit and 98 integration tests.

JUnit. 2021. *JUnit 4 documentation*. [Online]. Available at:
<https://junit.org/junit4/> [Accessed September 2026].
→ The 24 Android unit tests.

PayFast. 2026. *PayFast Developer Documentation*. [Online]. Available at:
<https://developers.payfast.co.za> [Accessed September 2026].
→ The signature calculation and callback verification in
`src/TheeBuhle.Infrastructure/Payments/PayFastGateway.cs`. The sandbox merchant
credentials in `appsettings.Development.json` are the public test values PayFast
publishes.

---

## 4. Third-party software

Used as distributed, through the package managers; none of it is modified or
copied into this repository. Version numbers are as pinned in
`Directory.Build.props` and `android/gradle/libs.versions.toml`. Each package's
authors, licence and source are on its package page, linked below.

### .NET (nuget.org)

| Package | Version | Used for |
|---|---|---|
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.12 | Database access and migrations |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.12 | Validating access tokens |
| System.IdentityModel.Tokens.Jwt | 8.16.0 | Issuing access tokens |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | Argon2id password hashing |
| HtmlSanitizer | 9.2.1039 | Stripping unsafe HTML from announcements |
| MailKit | 4.18.0 | Sending email over SMTP |
| Swashbuckle.AspNetCore | 10.2.3 | The OpenAPI/Swagger document |
| xunit, Microsoft.AspNetCore.Mvc.Testing, coverlet.collector | 2.9.3, 10.0.12, 6.0.4 | The test suites |

Package pages: `https://www.nuget.org/packages/<package name>`.

### Android (Maven Central and Google's Maven repository)

| Library | Version | Used for |
|---|---|---|
| Kotlin | 2.2.20 | The language and its compiler |
| Android Gradle Plugin | 8.13.2 | Building the app |
| AndroidX: core-ktx, appcompat, fragment, lifecycle, navigation, recyclerview, swiperefreshlayout, constraintlayout | see `libs.versions.toml` | Screens, navigation and lifecycle |
| androidx.security:security-crypto | 1.1.0-alpha06 | Encrypted storage of the session |
| com.google.android.material:material | 1.12.0 | Material 3 components |
| Retrofit / OkHttp (Square) | 2.11.0 / 4.12.0 | Calling the API |
| kotlinx.serialization | 1.7.3 | JSON |
| JUnit 4 | 4.13.2 | The unit tests |

### Web

Google Fonts. 2026. *Cormorant Garamond* and *Inter*. [Online]. Available at:
<https://fonts.google.com> [Accessed September 2026]. Both are released under
the SIL Open Font License.

Mermaid. 2024. *Mermaid 11.4.1*. [Online]. Available at:
<https://mermaid.js.org> [Accessed 21 September 2026].
→ Renders the entity-relationship diagrams produced by `docs/erd/generate.py`.

---

## 5. Client material

Thee Buhle Foundation. 2026. *Website & Mobile App Features*. Client brief
supplied with the INSY7315 project (`WIL.pdf`).

Thee Buhle Foundation. 2026. *@theebuhlefoundation*. Instagram. [Accessed 13
August 2026].
→ The wordmark, the tagline "Giving back, one child at a time & beyond", the
three values, and the events used in the demonstration data.

---

## 6. Declaration of AI assistance

**Your group must complete this section and confirm the wording your lecturer
expects.** Parts of this codebase were written with an AI coding assistant
(Anthropic's Claude, used through Claude Code) working from the group's
instructions, with the group reviewing, testing and deploying the result.
The IIE requires AI use to be declared, and the declaration belongs with the
submission as well as here.

A factual entry, to adapt:

> Anthropic. 2026. *Claude (Opus 5) [Large language model]*, used through Claude
> Code. Used for code generation, refactoring, test writing and documentation
> drafting throughout Part 2, under the direction and review of the project
> group.

---

## How this list is cited in the code

**Every C# file in `src/` and `tests/`, and every Kotlin file in `android/`,
opens with an attribution block** naming the sources
that shaped it. Where none did, it says so rather than inventing one:

```csharp
// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - OWASP (2024) Password Storage Cheat Sheet.
//    - Biryukov et al. (2021) RFC 9106: Argon2.
// =============================================================================
```

Migration files say instead that the schema operations were produced by the EF
Core tooling and reviewed, and that any hand-written SQL in them is own work.

Particular decisions are also cited in place, next to the line they explain:

```csharp
/// <summary>19 MiB, the OWASP minimum for Argon2id (OWASP, 2024; RFC 9106).</summary>
private const int MemoryKib = 19 * 1024;
```
