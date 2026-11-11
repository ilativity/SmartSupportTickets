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
   One display-name mapping in Core (`TicketStatusNames`), and a test that loads and re-saves `dataset.json` unchanged.
4. **Login rate limit vs the test suite.** Several end-to-end tests log in from one IP; a strict limit would make
   them fail randomly. → The limit is configurable and raised only in the `E2E` environment; an end-to-end spec
   checks that "too many attempts" is not shown as "wrong password".
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
| Core (domain, ports, use cases) — guard-breaking pass | 6 guards broken by hand. 5 tests failed as they should; **one did not**: removing the "client aborted → stop" rule still passed, because the next step (saving) also honours cancellation. The rule's own effect — not logging a client abort as an AI outage — was untested. | Test added that checks the log; the mutation now fails it. |
| Core — independent review | (1) the 10 s AI budget relied on the adapter honouring its cancellation token — a misbehaving adapter could hang ticket creation; (2) length limits checked before trimming, so `"  a "` passes a 2-character minimum; (3) composing an email ran outside the try/catch, so a template bug would turn a saved ticket into a 500; plus nits: link escaping, apostrophes trimmed from summaries, an emoji could be cut in half. | (1) hard timeout (`WaitAsync`) + test with an adapter that ignores cancellation; (3), link, apostrophe, emoji fixed with tests; each new guard broken by hand to see its test fail. (2) is HTTP validation → fixed and tested in the API commit. Emails after two very quick edits may arrive out of order (each one correct) — accepted, noted in README. |
| JSON storage adapter — guard-breaking + independent review | Broken by hand: lock removed from update, cache updated before the file write, corrupt file treated as empty, default date format — each caught by a test. Review: **a mistyped seed path would silently create an empty, permanent data file** (fixing the path later would never re-seed); records with a missing field gave unhelpful errors; the "no write without change" test relied on file timestamps. | Missing configured seed → clear startup error; no seed → no file until the first ticket; missing fields reported with the file name; timestamp test replaced with a content marker. Both new guards broken by hand to see their tests fail. File locked by antivirus on Windows → request fails, no data loss — accepted. |

| HTTP API (endpoints, validation, masking) — guard-breaking + independent review | Broken by hand: email shown unmasked, length measured before trimming, display-name and dot-less emails accepted, client errors reported as 500 — each caught. Writing the tests found a real bug: **a form without the expected fields returned 500** (fixed). Review: no test sent real `multipart/form-data` (what the browser sends); CORS ran after the error handler, so a cross-origin page could not read error bodies; quoted and non-ASCII email forms accepted; line breaks allowed in the name; masking could split an emoji. One finding was wrong: it said the "400 not 500" rule was untested — breaking it does fail a test. | All fixed with tests except: seed path in Docker → handled in the Docker commit; client errors logged at Error level → accepted. |
| Authentication (JWT admin, login limit, secrets policy) — guard-breaking + **stricter independent review** (security code) | Broken by hand: edit endpoint opened to all, any logged-in user accepted instead of Admin, defaults allowed in Production, rate limit removed, everyone treated as admin — each caught. One guard was **not** caught: pinning the token algorithm (unsigned tokens were already refused by the library) → added a test with a token signed by the same key under another algorithm, which now fails without the pin. Review: per-IP limit alone does not stop guessing from many addresses; the public dev key with extra spaces passed the production check; a truncated password hash started fine and then refused every login; token lifetime unchecked; password visible in shell history; some tests asserted "any exception". While fixing, my first hash check was itself too weak (a truncated hash still passed) — the new test caught it. | All fixed with tests and each new guard broken by hand: shared ceiling on all logins, "contains" check for the dev key, structural hash check at startup, lifetime 1–1440 min, password read from standard input, exact error messages asserted, wrong-audience and Staging/E2E cases added. Behind nginx every client shares the proxy's IP → trusted-proxy forwarding is configured in the Docker commit. |
| Gemini adapter — tests + independent review | Model names were **checked against Google's and OpenAI's current documentation** first: the ones the assistant remembered were already retired. Writing the tests found a real bug: an answer with an empty `candidates` list (Google's reply when it blocks a prompt) threw instead of meaning "no summary". Review: a key with a trailing newline (common from secret files) would make every request fail silently; a proxy base URL with a path would lose that path; no test went through the real dependency-injection wiring (URL, key, model from configuration). | Fixed with tests; the wiring test replaces only the network under the real configured client. Each fix broken by hand to see its test fail. Live call with a real key not possible here — left to the owner's manual check. |
| Frontend (list, new ticket, details, login, admin edit) — two independent reviews + real browser | Checked in a real browser at 1280 px and 375 px (screenshots), which found: focus not moving to the first invalid field and not entering the form on open (React's `autoFocus` is not the HTML attribute `<dialog>` uses). Review 1: **Back/Forward left the search box and the URL disagreeing with the list**; server errors for fields the form does not show were silently dropped; a non-JSON 200 crashed with a raw parse error; long words could overflow cards on phones. Review 2 (auth in the UI): a save finishing after logout could put a full customer email back into the cache; a late 401 for an old token could log out a new session; an expired session kept sending its token; the return-to path accepted `//other-site`. | All fixed. The URL is now the single source of truth for the list. e2e tests were then checked by breaking guards: the 429 message guard was caught; **the Back/Forward test was not** (it used the same search value before and after) — rewritten so the mutation fails it. |
| OpenAI adapter — guard-breaking | Two filters survived being broken by hand: the "only `output_text` parts" filter (a refusal has no `text`, so it changed nothing) and the "only message items" filter (the test's reasoning item had no text, unlike real ones). | The first filter was removed as untestable dead code, with the reason in a comment; the test data was made realistic so the second one is now caught. |
| SMTP adapter — real SMTP in tests + independent review | MailKit 5 changed `SendAsync` (the assistant's knowledge was of 4.x) — caught by the compiler, fixed from the package's own documentation. Review: a goodbye (`QUIT`) failing after the server accepted the mail would have logged a delivered email as lost; the deadline did not cover DNS/connect on every path; a malformed sender address, a bad port, or a password over an unencrypted connection would only fail per ticket; the timeout test could pass on its own timer. | All fixed with tests; the deadline guard broken by hand to see its test fail. A tiny SMTP server inside the tests lets MailKit really deliver a message and the test read it. |
| Image upload — guard-breaking + **stricter independent review** (security code) | Broken by hand: the byte check, `nosniff`, the WebP signature check — each caught. My first attempt to break the strict file-name pattern was a faulty edit that changed nothing, which showed up as "not caught"; redone precisely, 4 tests failed. Review found no path traversal or script execution, but: an image could be left on disk when the ticket failed to save, and a cancelled write could leave half a file; **anonymous creation had no rate limit**, so a script could fill the disk with 5 MB images; the name pattern used `$` (which in .NET also accepts a trailing line break); two "cannot download" tests would pass without the guard (the files did not exist anyway). | Clean-up of orphaned and partial files, a per-IP limit on creating tickets, `\z` instead of `$`, decoy files placed where the guard really has to stop them; each new guard broken by hand to see its test fail. Not testable: `\z` vs `$` (a name with a line break cannot exist as a file, so a test cannot tell them apart) — kept as the correct rule. The 413 for an oversized body is enforced by the real server, not the in-memory test host, so it is not covered by a test. |
| Inline admin editing (table + phone cards) — real browser + independent review | Checked at 1280 and 375 px: phone cards had no visible labels for the controls (added). Review found **two real bugs**: an edit typed while a save was running was thrown away; and comparing trimmed text while typing swallowed a trailing space, so "Fixed again" could not be typed after a saved "Fixed". Also: edits in rows hidden by a filter were saved unseen; an edit to one field could write back a stale value of the other; old values flashed after saving; the toast covered the Save bar; e2e tests used `fill` (which would pass even if the row's link overlay swallowed real clicks). | State reworked: only touched fields, kept exactly as typed; "changed" computed against the latest server values; only visible rows are saved, with fresh values for untouched fields; an edit is cleared only if unchanged since it was sent; rows refresh before edits clear. New e2e tests (real click, trailing space, partial failure with a stubbed 500, hidden-by-filter); the trailing-space and hidden-row guards were broken by hand and caught. Not covered by a test: typing during a save (timing-dependent) — handled by comparing the exact edit object that was sent. |
| Docker (API, nginx, compose) — run for real | `docker compose up` was actually run and tested through nginx (health, SPA routes, 401 without a token, the data file not downloadable, upload, size limits, data surviving a restart). That found two real problems no unit test could: **the API refused to start in the demo setup**, because compose passes an empty string for an unset secret and the development defaults only replaced a missing one (fixed: empty counts as missing, with a test); and the lock file generated on Windows was missing Linux-only optional packages, so the image build failed (lock file regenerated in a Linux container, still installs cleanly on Windows). Per the earlier security review, forwarded client addresses are trusted only from the compose network — with a test that a forged header from elsewhere buys no extra login attempts. | Fixed and verified in the running containers. |
| Admin adds an image later; required-field markers (owner's feedback) — **stricter review** + repeated test runs | Running the whole e2e suite several times revealed a **flaky test** (1 failure in 5 runs): it read the list right after the filter changed, while the previous rows were still shown on purpose. Review found no authorization or file-handling hole, but **a real bug: uploading the image silently threw away a status/resolution edit the admin had typed but not saved** (the edit form restarted whenever the ticket's update time changed); also a missing logout guard for cached admin data, a stale page after a 409, a leading-slash URL counted as "no image", a leaked preview URL, a misplaced doc comment. | All fixed. The flaky check became a retrying assertion (80 repeated runs green); a new e2e test types a resolution, uploads an image and expects the text to survive — breaking the fix by hand makes it fail. Race between two admins tested deterministically (the other admin acts exactly between our file save and our ticket update). |
---

## 6. Honest limits

This process **reduces** the number of errors; it cannot prove there are none. What it does guarantee is that
the important decisions are visible and reasoned, the rules that matter are tested on the inputs they must
reject, and nothing in the repository is code its author has not read.
