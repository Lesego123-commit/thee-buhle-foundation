# Accessibility

Target: **WCAG 2.2 level AA** (W3C, 2023; see [references.md](references.md)) on the website, the staff console and the Android
app. This page records how that was checked, what the audit found, and what
was changed.

## How the website was audited

Every public page and the staff console were checked at two widths, 1280px
(desktop) and 375px (a small phone), with a script run in the browser against
the rendered page. It checks:

| Check | WCAG | Rule applied |
|---|---|---|
| Text contrast | 1.4.3 | 4.5:1 for normal text, 3:1 for large text. Semi-transparent text is blended with its background first, since otherwise it looks darker than it really is |
| Target size | 2.5.8 | Interactive targets at least 24×24px, unless inline in a sentence |
| Accessible names | 4.1.2 | Every button, link and field has a name a screen reader can announce |
| Heading order | 1.3.1 | No skipped heading levels |
| Reflow | 1.4.10 | No horizontal page scroll at 375px |

Pages: `/`, `/programmes`, `/programmes/mentorship`, `/impact`, `/events`,
`/news`, a news article, `/donate`, `/volunteer`, `/contact`, `/privacy`,
`/safeguarding`, the 404 page and `/admin`.

## What it found, and what changed

| Finding | Where | Fix |
|---|---|---|
| Gold text at 3.2:1 | Eyebrows, "Learn more" links, badges on every page | `--gold` deepened from `#A87C32` to `#856228` (5.1:1 on cream). The lighter gold stays only in decorative gradients, which carry no text |
| Grey caption text at 3.4:1 | Dates, captions, donation allocation | `--ink40` deepened from `#8E887C` to `#6D685E` (5.1:1 on cream) |
| Footer headings jumped from `h1`/`h2` to `h5` | Every page | Footer column titles are now `h2`, styled as before |
| Footer, announcement-bar and "Learn more" links 15–18px tall | Every page | `min-height: 24px` on those links |
| "per month" fixed grey on the selected (dark green) amount button, 2:1 | `/donate` | The sub-label inherits the button's colour, so it passes on both states |
| Wide tables scroll sideways but could not be reached by keyboard | `/impact`, `/privacy`, `/volunteer` | The scroll wrapper is a labelled region with `tabindex="0"` and a visible focus ring |
| Page 631px wide at 375px | `/volunteer` | Grid children get `min-width: 0`, so the availability table scrolls inside its own region instead of stretching the page |

After the changes every page passes at both widths. What the script still
reports is explained below.

### Remaining reports, and why they are not failures

- **Checkboxes measure 17×17px.** Each one sits inside its `<label>`, so the
  whole label row is the click target, well over 24px.
- **The phone number on `/safeguarding`.** It is a link inside a sentence,
  which 2.5.8 exempts.
- **Two unnamed buttons on `/events`.** They are inside a closed `<details>`
  and are not rendered until it opens. When open they are named.

## Android app

Checked with Android lint (its accessibility checks run as part of
`lintDebug`) and by hand:

| Finding | Fix |
|---|---|
| Gold and grey text colours same as the website's old values | `colors.xml` uses the same deepened values |
| Text at 9–10sp (sign-in wordmark, message and resource labels) | Raised to 11sp, the smallest lint accepts |
| Wordmark "THEE BUHLE / FOUNDATION" hard-coded and read letter by letter | Moved to string resources; the pair is announced once, as "Thee Buhle" |
| Parental consent could not be given from the app | New dialog on Home: tap a form under "Waiting for your answer" to see the whole form, then **Give permission**, **Decline** or **Not now**. Decline is offered as plainly as agreement. A learner's own account sees the form but is told a guardian must answer |

Material components provide 48dp touch targets and TalkBack labels for the
navigation, chips and buttons. The app is built on `sp` units throughout, so it
follows the system font size.

Lint now reports no errors. The remaining warnings are dependency version
notices, unused resources and overdraw, none of them accessibility issues.
