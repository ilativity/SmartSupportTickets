# AI-assisted development — and how its failure modes were guarded against

The assignment allows AI tools "as long as you can explain every line of code". This project was built with
an AI coding assistant (Claude Code) as a pair programmer. This document explains **how** it was used, and
what was done so that the speed of AI did not come at the cost of correctness.

The short version: **AI writes plausible code fast; plausible is not correct.** The risk is not syntax — the
compiler catches that — but *logic and design* errors that look right line by line. Every step below exists to
catch one specific kind of such error.

---

## 1. Who decided what

The architecture and scope decisions were mine; the assistant proposed options with trade-offs, I chose.
Examples: forking the provided repository, hexagonal architecture, .NET 9 (as the brief asks, although it left
support on 2026-11-10), which bonuses to build, editing both inline and on the detail page (as the mockup
shows), synchronous AI summary (as the brief words it), and delivering in two stages so that Stage 1 alone is a
complete solution. All of them are recorded with reasons in [SPEC.md §1](SPEC.md) and the README.

---

## 2. Known failure modes of AI-generated code, and the countermeasure for each

| Failure mode | Why it happens | Countermeasure used here |
|---|---|---|
| **Plausible but wrong logic** — e.g. a race condition where each line is correct | The model optimises for code that looks right; concurrency, privacy and failure paths are not visible in any single line | **Spec before code.** Every rule is written down as a numbered decision with its reason ([SPEC §1](SPEC.md)) and reviewed by me before implementation — a design error costs one line to fix on paper. |
| **Tests share the author's assumptions** — many green tests, bug still there | The same "mind" writes the code and the tests, so both encode the same blind spot | Every rule is tested **with the input it must reject**, not only the happy path; and before committing, each guard is **deliberately broken by hand to see its test fail** — this tests the test. |
| **Self-review blind spot** | The context that wrote the code reviews it with the same reasoning | Before larger commits, an **independent review in a fresh context** that sees only the diff, not the reasoning behind it; security-sensitive code (authentication) gets the strictest review. Findings are fixed or answered, not ignored. |
| **Ambiguity resolved silently** | The model picks *an* interpretation and moves on | Ambiguities in the brief are surfaced and decided explicitly, e.g. "customers can edit" vs "only logged-in users can edit" → D3; "the list is public" → does that expose every customer's email? → D4. |
| **"Works in my head"** | Code is judged by reading, not by running | **End-to-end tests against the real backend** (Playwright, nothing mocked except deliberate failure cases) plus a manual walk-through ([SPEC §12](SPEC.md)): open the actual email text and follow its link, check the 375 px phone width, look at screenshots. |
| **More code than a human can follow** | Generating is cheap; understanding is not | **Small commits, one logical change each**, so every commit can be read in 10–15 minutes. **I read every commit**; any line I could not explain was either simplified or explained before moving on. Deliberately no MediatR, domain events or generic Result types — plain interfaces deliver the architecture. |
| **Insecure defaults** (fail-open) | A default value that makes the demo "just work" also works in production | Secrets are never in the repo; outside `Development`/`E2E` a missing JWT key or admin password hash **stops the application from starting** (D13). |
| **Hallucinated or outdated APIs and versions** | Training data lags behind libraries | Library documentation was checked against current sources; the build and tests are the final judge, and failures are reported as they are, with output — never "should work". |
| **Docs drift from code** | Code changes, README does not | README and SPEC are updated **in the same commit** as the code they describe. |

---

## 3. Errors caught at design time (before any code was written)

These are real examples from this project — each would have been easy to ship:

1. **Customer emails exposed to everyone.** The ticket list is public, so returning the email in it would leak
   every customer's address. → The list never contains the email; the detail view masks it unless an admin asks
   (D4).
2. **Race condition on update.** Reading a ticket, changing it and saving it as three separate steps lets two
   simultaneous admin edits overwrite each other and send wrong notification emails. → The whole
   read-modify-write runs inside the repository lock and returns the *before/after* pair from which emails are
   derived (D15).
3. **`"In Progress"` contains a space.** The default enum converter cannot read the provided dataset. →
   Explicit member name, and a test that loads and re-saves `dataset.json` unchanged.
4. **Login rate limit vs the test suite.** Several end-to-end tests log in from one IP; a strict limit would make
   them fail randomly. → The limit is configurable and raised only in the `E2E` environment; a separate test checks
   that "too many attempts" is not shown as "wrong password".
5. **Form uploads and anti-forgery.** ASP.NET Core requires anti-forgery tokens for form binding by default. The
   API uses bearer tokens, not cookies, so CSRF does not apply — disabling it is a reasoned decision, commented in
   code, not a workaround.
6. **Uploaded files.** Validating a file's extension does not make its bytes safe. → Type is decided by magic
   bytes, the file is stored under a new GUID name, and the client's file name is never used.

Before writing code, publicly available solutions to the same assignment were also read — for ideas and
pitfalls only, no code was reused. Items 1 and 2 above appeared in them, which is how they became explicit
decisions rather than lucky accidents.

---

## 4. The loop for every commit

```
SPEC decision ─▶ implement ─▶ tests (incl. rejected inputs) ─▶ break each guard, see its test fail
      ▲                                                                   │
      │                                                                   ▼
 update SPEC/README ◀── I read and can explain every line ◀── independent review ◀── run it for real
```

---

## 5. Verification log

Filled in as the work progresses: what the independent reviews found and what happened to each finding.

| Commit | Review findings | Outcome |
|---|---|---|
| | | |

---

## 6. Honest limits

This process **reduces** the number of errors; it cannot prove there are none. What it does guarantee is that
the important decisions are visible and reasoned, the rules that matter are tested on the inputs they must
reject, and nothing in the repository is code its author has not read.
