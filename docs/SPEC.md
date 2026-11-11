# SPEC — Smart Customer Support System

Source assignment: [docs/assignment/ASSIGNMENT.md](assignment/ASSIGNMENT.md)
(upstream: https://github.com/oringilad/Dotnet_React_EX). Status: **approved** (2026-11-11).

Repository: **fork** of the upstream, renamed `ilativity/SmartSupportTickets` — the history shows the
solution is built on the provided skeleton (upstream contained only the task, dataset, images and an empty
`.slnx`). The upstream README moved to `docs/assignment/`; the root README describes the solution.

The build follows this file. Every decision here is repeated with its reason in the final `README.md`
("Design decisions" section) — the reviewer must be able to read *why*, not only *what*.

---

## 1. Decisions

| # | Decision | Reason |
|---|----------|--------|
| D1 | Backend targets **net9.0**, ASP.NET Core **Minimal API** | Assignment asks for .NET 8/9 and prefers Minimal API. .NET 9 left support 2026-11-10 → README names .NET 10 LTS as the next step. |
| D2 | **Hexagonal architecture (ports & adapters)** in three projects: `Core` (domain + ports + use cases, no framework), `Adapters` (driven: JSON file, AI, email, files), `Api` (driving: HTTP + composition root) | The dependency rule is enforced by the **compiler** (Core has no reference to ASP.NET, MailKit or `HttpClient`), not by convention. Scaling or changing storage = new adapter, Core untouched (see §11). Deliberately light: ports are plain interfaces — no MediatR, domain events or Result types. |
| D3 | Anyone may **list, view, create**; only **Admin (JWT)** may **edit** | Resolves the README conflict ("customers view and edit" vs bonus "only logged users can edit"). |
| D4 | Public responses **never expose a full email**: the list omits it, details mask it (`j***@example.com`) unless the caller is Admin | The list is public and lists every ticket id, so "knowing the id" is not a secret. |
| D5 | JSON file storage behind the `ITicketRepository` port: **in-memory cache + `SemaphoreSlim` + atomic write** (temp file → replace) | Concurrent requests must not corrupt the file. |
| D6 | Runtime data lives in `App_Data/` (git-ignored), **seeded from the repo's `dataset.json`** on first run | Running the app never dirties the committed dataset; reset = delete `App_Data`. |
| D7 | AI summary port with **Gemini** and **OpenAI** adapters + `None`, chosen by `Ai:Provider` | Both providers named in the assignment. No key → ticket still created, `summary = null`. |
| D8 | Notification port with **Console** (default) and **Smtp** (MailKit, Gmail App Password) adapters, chosen by `Email:Provider` | Mock is required, real SMTP is a bonus. Reviewer runs it with zero setup. |
| D9 | **AI/email failures never fail the request** — logged, request succeeds | A ticket is the customer's data; a third-party outage must not lose it. |
| D10 | Status + resolution changed in one save → **one** email listing both changes; no change → no email | Assignment lists 3 triggers; two emails for one click is noise. |
| D11 | Optional **image upload** on create | Dataset has `imageUrl`, mockup has "Upload an image". |
| D12 | Frontend: **Vite + React + TypeScript + Tailwind**, React Router, TanStack Query. Only `src/api/` knows about `fetch`/HTTP | Typed API contract, cached server state, URL-addressable `/tickets/:id`. The `api/` module is the frontend's adapter boundary — a full hexagon in React would be overkill. |
| D13 | Secrets only via `dotnet user-secrets` / env vars. **Only `Development` and `E2E` have built-in defaults; any other environment with a missing JWT key or admin hash refuses to start** | Public repo; a missing secret must fail closed, never run with a default key. |
| D14 | Code comments: XML doc on public types/members + comments that explain **why** (not what) | Every line must be explainable; comments carry the reasoning. |
| D15 | Update = **read-modify-write inside the repository lock**; the repository returns the `(before, after)` pair and emails are derived from it | Reading state outside the lock lets two concurrent admin edits race and send wrong/duplicate emails. |
| D16 | Admin edits **both** inline in the list (as in `ticket-manage.png`) **and** on `/tickets/:id`, on desktop and mobile | The mockup shows inline management; the brief requires editing on the detail view. Both call the same `PUT`. |
| D17 | AI summary is generated **synchronously** during create (10 s budget) | The brief says "while saving the ticket, call an AI API to generate and display a short summary". Cost: Submit waits ~1–3 s, shown with a spinner. |
| D18 | `GET /api/tickets/statuses` serves the status list | The frontend never hard-codes `"In Progress"`; one source of truth. |
| D19 | **Docker**: Dockerfiles for API and frontend + `docker-compose.yml` (1 API replica), `/health` endpoint | Reviewer runs everything with one command without installing .NET/Node. One replica because file storage is single-instance — stated honestly in README "Scaling". |
| D20 | Time and ids come through ports (`TimeProvider`, `IIdGenerator`) | Use-case tests are deterministic. |

---

## 2. Repository layout

```
SmartSupportTickets/
├─ MX_EX.slnx                          # provided solution file; all .NET projects added to it
├─ dataset.json                        # provided seed data (never written at runtime)
├─ docker-compose.yml
├─ README.md                           # run + design decisions + scaling + known limitations + screenshots
├─ docs/  SPEC.md · AI-ASSISTED-DEVELOPMENT.md (how AI errors were guarded against) · assignment/
├─ backend/
│  ├─ SupportTickets.Core/             # THE HEXAGON — BCL + Microsoft.Extensions.Logging.Abstractions (an interface, to log D9 failures)
│  │  ├─ Domain/        Ticket · TicketStatus + TicketStatusNames · TicketChange · TicketRules (field limits)
│  │  ├─ Ports/In/      ITicketUseCases (List, Get, Create, Update) + commands/query; statuses come from TicketStatusNames
│  │  │                 (CreateTicketCommand, UpdateTicketCommand, TicketQuery)
│  │  ├─ Ports/Out/     ITicketRepository · ISummaryGenerator · INotificationSender · IIdGenerator · IImageStore
│  │  └─ Application/   TicketUseCases (implements Ports/In using Ports/Out) · NotificationComposer (email texts) · SummaryRules
│  ├─ SupportTickets.Adapters/         # DRIVEN adapters — implement Ports/Out; named by technology
│  │  ├─ Persistence/   JsonTicketRepository · JsonStorageOptions
│  │  ├─ Ai/            GeminiSummaryGenerator · OpenAiSummaryGenerator · NoSummaryGenerator · AiOptions
│  │  ├─ Notifications/ ConsoleNotificationSender · SmtpNotificationSender · EmailOptions
│  │  ├─ Files/         LocalImageStore (size + magic-byte check, GUID names)
│  │  └─ DependencyInjection.cs   # AddAdapters(config): picks adapters by Ai:Provider / Email:Provider
│  ├─ SupportTickets.Api/              # DRIVING adapter (HTTP) + composition root
│  │  ├─ Program.cs     # wiring only: AddCore, AddAdapters, auth, CORS, rate limit, ProblemDetails, MapXxx()
│  │  ├─ Endpoints/     TicketEndpoints · AuthEndpoints · HealthEndpoints
│  │  ├─ Dtos/          HTTP contract: TicketListItemDto · TicketDetailsDto · CreateTicketRequest ·
│  │  │                 UpdateTicketRequest · LoginRequest · LoginResponse  (+ mapping, email masking)
│  │  ├─ Auth/          JwtOptions · AdminOptions · TokenService · AdminCredentialsVerifier
│  │  ├─ Validation/    ValidationFilter<T> (DataAnnotations for minimal APIs)
│  │  ├─ Tools/         HashPasswordCommand (`dotnet run -- hash-password`, password from stdin; no side effects on load)
│  │  ├─ Dockerfile
│  │  └─ appsettings.json · appsettings.Development.json · appsettings.E2E.json
│  └─ SupportTickets.Tests/            # xUnit, no mocking library
│     ├─ Fakes/         InMemoryTicketRepository · FakeSummaryGenerator (can throw) · FakeNotificationSender (outbox)
│     │                 · FixedIdGenerator   (FakeTimeProvider from Microsoft.Extensions.TimeProvider.Testing)
│     ├─ Core/  · Adapters/  · Api/  · Architecture/
└─ frontend/                           # Vite React TS
   ├─ Dockerfile · nginx.conf
   ├─ e2e/                             # Playwright
   └─ src/
      ├─ api/        client.ts (fetch wrapper, auth header, ProblemDetails → ApiError, 429 ≠ "wrong password") · tickets.ts · auth.ts · types.ts
      ├─ auth/       AuthContext.tsx
      ├─ pages/      TicketsPage · TicketDetailsPage · LoginPage · NotFoundPage
      ├─ components/ TicketTable · TicketCards · TicketFilters · NewTicketModal · StatusBadge · StatusSelect
      │              · SaveBar · Header · Toast · ErrorState (Retry) · EmptyState
      └─ hooks/      useDebouncedValue · useInflightGuard (useRef-based double-submit guard)
```

Dependency direction: `Api → Core`, `Api → Adapters` (composition root only), `Adapters → Core`. **Core → nothing.**
Fakes live in the test project, not in Core, so no test code ships in the production assembly.

---

## 3. Domain (Core)

`Ticket` (matches `dataset.json` field names, camelCase on disk):

| Field | Type | Notes |
|---|---|---|
| id | `Guid` | from `IIdGenerator` |
| name | `string` | 2–100 chars |
| email | `string` | valid address, ≤ 254 |
| description | `string` | 10–2000 chars |
| summary | `string?` | AI output, ≤ 200 chars, null if unavailable |
| imageUrl | `string?` | `uploads/{guid}.{ext}`; seed values point to files that don't exist → UI shows a placeholder |
| status | `TicketStatus` | `New`, `In Progress`, `Resolved`, `Closed` — display names live once in Core (`TicketStatusNames`); the JSON adapter and the API both use them, so no serializer attribute is needed in Core. Parsing accepts display names only (not `InProgress`, not numbers) |
| resolution | `string` | ≤ 2000, `""` when empty (as in dataset) |
| createdAt / updatedAt | `DateTimeOffset` | UTC from `TimeProvider` |

`Ticket` is an **immutable record**. `Ticket.Apply(status, resolution, now)` returns the **same instance** when
nothing changes (whitespace-only edits included), else a new one with `updatedAt = now`; `TicketChange.Between(before,
after)` says what changed — the domain, not the endpoint, decides what counts as a change. No status-transition rules (any → any): the assignment defines
no workflow; noted as an extension.

---

## 4. API (`/api` prefix, JSON, errors = RFC 7807 ProblemDetails)

| Method | Route | Auth | Body / query | Success | Errors |
|---|---|---|---|---|---|
| GET | `/api/tickets` | — | `?status=In Progress&search=laptop` (search: name or description, case-insensitive) | 200 `TicketListItemDto[]`, newest first | 400 bad status |
| GET | `/api/tickets/statuses` | — | — | 200 `string[]` in workflow order | |
| GET | `/api/tickets/{id:guid}` | optional | — | 200 `TicketDetailsDto` (email masked unless Admin) | 404 |
| POST | `/api/tickets` | — | `multipart/form-data`: name, email, description, image? | 201 + `Location` + `TicketDetailsDto` | 400 validation, 413 too large |
| PUT | `/api/tickets/{id:guid}` | **Admin** | JSON `{ status, resolution }` | 200 `TicketDetailsDto` | 400, 401, 403, 404 |
| POST | `/api/auth/login` | — (rate-limited, configurable; default 5/min/IP, raised in `E2E`) | `{ username, password }` | 200 `{ token, expiresAt, username }` | 401, 429 |
| GET | `/uploads/{file}` | — | — | image | 404 |
| GET | `/health` | — | — | 200 `Healthy` | 503 |
| GET | `/openapi/v1.json` (+ Scalar UI in Development) | — | | | |

DTOs (Api project): `TicketListItemDto` — id, name, description, summary, status, resolution, hasImage,
createdAt, updatedAt, **no email** (D4). `TicketDetailsDto` — all fields; `email` masked for non-admin.

Implementation notes:
- Endpoints grouped with `MapGroup`, `TypedResults`, thin: bind → DTO → command → use case → DTO.
- Validation: DataAnnotations on request records, run by `ValidationFilter<T>` → `TypedResults.ValidationProblem`.
  Length limits apply to the **trimmed** value (Core trims): `"  a "` must fail the 2-character minimum.
- `POST` uses `[FromForm]` → `.DisableAntiforgery()`: bearer tokens, no cookies, so CSRF does not apply (commented in code).
- Image: jpg/png/webp, ≤ 5 MB, type decided by **magic bytes**; the adapter writes a new file under a GUID name
  from the validated bytes only — the client's file name and content-type are never kept.
- Any non-OK answer from an AI provider (incl. 429) is an error → null summary, never an empty string.

---

## 5. Use cases and adapters

**Create:** validate (Api) → `IImageStore.SaveAsync` (if any) → `ISummaryGenerator.SummarizeAsync(description)`
with a 10 s budget; exception/timeout → log + null → `repository.AddAsync` → `INotificationSender` "Ticket received"
with tracking link `{App:FrontendBaseUrl}/tickets/{id}` (send failure → log only) → 201.

**Update (Admin):** `repository.UpdateAsync(id, apply)` runs entirely under the lock (D15): load (404) →
`Ticket.Apply(...)` → nothing changed → keep as-is → else set `updatedAt` and write the file → return `(before, after)`.
Outside the lock the use case composes one email listing what changed (D10) → 200. A failed file write leaves the
in-memory cache untouched.

**AI prompt:** fixed system instruction ("summarize the support issue in one sentence, max 15 words, same
language as the input, output only the summary"); the customer text goes in a separate, delimited user message
and is treated as data. The answer is checked in Core (`SummaryRules`, same for every provider): whitespace and
wrapping quotes removed; > 40 words → discarded (null); otherwise capped at 200 chars.
Models configurable (`Ai:Gemini:Model`, `Ai:OpenAI:Model`), defaults = the cheapest fast model of each provider.
Typed `HttpClient`s, plain REST — no SDK, so every line is explainable.

**Notifications:** `Notification(to, subject, textBody, htmlBody)`. Console adapter logs it; Smtp adapter =
MailKit, `smtp.gmail.com:587` StartTLS, credentials from user-secrets.

**Persistence:** singleton `JsonTicketRepository`: lazy load once, all reads/writes under `SemaphoreSlim(1,1)`,
write = serialize to `tickets.json.tmp` → `File.Move(overwrite: true)`. Tickets are immutable records, so the cache can hand them out without copying.

**Auth (Api):** `Auth:Admin:Username` + `Auth:Admin:PasswordHash` (ASP.NET `PasswordHasher` format). Login always
runs hash verification, even for an unknown username (against a fixed dummy hash), so timing does not reveal
whether the user exists. JWT HS256, `Jwt:Issuer/Audience/SigningKey`, 60 min, `role=Admin`, policy `AdminOnly`.
Development/E2E defaults: `admin` / `Admin123!` and a dev signing key (README says so); elsewhere missing values →
startup error (D13).
Startup also refuses: a key shorter than 32 chars, the public dev key (even padded) outside Development/E2E, a hash
that is not a complete Identity V3 hash, `Jwt:LifetimeMinutes` outside 1–1440. Login limits: per IP (default 5/min)
**and** a shared ceiling on all logins (default 30/min). Behind nginx (Docker) forwarded client IPs are trusted only
from the compose network, otherwise every client would share the proxy's IP.

**Cross-cutting:** `AddProblemDetails` + `UseExceptionHandler`; CORS for `App:FrontendBaseUrl`; rate limiter
registered after CORS so a 429 is readable by the browser; structured logging; health checks.

---

## 6. Frontend

- `/` → `/tickets`. **TicketsPage:** status dropdown + search (debounced 300 ms), mirrored into the URL query.
  - **Desktop (≥ md): table** — Name · Summary · Description (clamped, expandable) · Status · Resolution · Updated.
  - **Mobile (< md): cards** — same data, denser; no horizontal scroll at 375 px.
  - Visitors: the whole row/card is one real `<a>` to `/tickets/:id` (keyboard and middle-click work).
  - **Admin (D16):** Status becomes a dropdown and Resolution a text field in each row/card; changed items are
    highlighted; a **Save changes** bar (sticky at the bottom on mobile, appears only when something is dirty)
    sends one `PUT` per changed ticket and reports per-item failures. The name stays a link to the detail page.
  - Status options come from `/api/tickets/statuses`. Load errors → message + **Retry**.
  - **New ticket** button → `NewTicketModal` (native `<dialog>`, Esc closes, focus returns; full-screen sheet on
    mobile; field errors from ProblemDetails; image preview; spinner while the AI summary is generated; success
    toast with a link).
- **TicketDetailsPage** `/tickets/:id`: id (copy button), customer, description, AI summary, image (placeholder if
  missing), status, resolution, dates. Admin: status select + resolution textarea + Save (enabled when dirty).
  Non-admin: read-only + "Log in to edit". Unknown id → not-found page.
- **LoginPage** `/login`: returns to the previous page; 429 shows "too many attempts", not "wrong password".
- Every async button (Submit, Save, Save changes, Login) is disabled from the first click by a `useRef` guard.
- Token in `sessionStorage`, attached by `api/client.ts`; a 401 logs out.
- Dev: Vite proxy `/api`, `/uploads` → backend. Production (Docker): nginx serves the build and proxies the same paths.
- Tailwind with own palette tokens; one project-specific visual detail instead of template look; labels, focus
  rings, AA contrast; `overflow-x` guard set on `html` (not only `body`).

---

## 7. Tests — a reasonable amount

Principle: every required flow and every decision in §1 that is a rule has at least one test, **including the
input it must reject**; no tests for framework behaviour or trivial getters. Before committing, each guard is
briefly broken by hand to see its test fail.

**Backend (xUnit, `SupportTickets.Tests`):**
- *Core* (use cases with fakes, `FakeTimeProvider`): create sends 1 email with the tracking link; AI throws or times
  out → ticket saved with null summary; status-only / resolution-only / both → exactly 1 email; no change → 0;
  notification sender throws → request still succeeds.
- *Adapters:* JSON repository — `dataset.json` load → save round-trip keeps every field, status spelling and date
  format; 20 parallel adds → valid file with all 20; 2 concurrent updates → both applied, each sees the correct
  `before`. Image store — accepts a real PNG; rejects a `.png` with text content and a file > 5 MB. AI adapters —
  with a stub `HttpMessageHandler`: success parsed; 429/500 → null; 60-word answer → null.
- *Api* (`WebApplicationFactory`, temp `App_Data`): anon POST 201; anon PUT 401; admin PUT 200; filter + search;
  unknown id 404; list has no email; details email masked for anon and full for admin; invalid body 400 with field
  errors; login wrong password and unknown user → 401; non-Development environment without signing key → startup fails.
- *Architecture:* Core's assembly references no `Microsoft.AspNetCore.*`, `MailKit`, `System.Net.Http`.

**Frontend end-to-end — Playwright** (`frontend/e2e/`, Chromium, local workers capped, `exact: true` role names):
- `webServer` starts the real backend on **5210** (`ASPNETCORE_ENVIRONMENT=E2E`: fresh temp data dir seeded from
  `dataset.json`, `Ai:Provider=None`, `Email:Provider=Console`, high login limit) and Vite on **5211** — separate
  from the dev ports, nothing mocked except where a spec stubs a failure with `page.route`.
- Specs: (1) list, status filter + search, URL sync; (2) anonymous create via the modal incl. validation errors,
  lands on `/tickets/:id`; (3) anonymous details: masked email, no edit controls; (4) admin edits on the detail
  page, survives reload; (4b) admin edits two rows inline and saves both with one click; (5) wrong password →
  error, 429 → "too many attempts" (stubbed); (6) unknown id → not-found; (7) mobile 375 px: no horizontal scroll,
  cards, modal usable, admin edits a card via the sticky Save bar; (8) API down on list load → Retry works (stubbed).

---

## 8. Ports (local)

| Use | Port |
|---|---|
| API (dev) | 5200 |
| Vite (dev) | 5201 |
| API (e2e) | 5210 |
| Vite (e2e) | 5211 |
| Docker: web (nginx) | 8080 → container 80; API not published, reached through nginx |

---

## 9. Delivery plan — two stages, one commit per logical change

Deadline: Monday 2026-11-16, afternoon. The assignment is sized at ~4 h; this plan is ~7–11 h, so it is split
so that **the end of Stage 1 is already a complete, submittable solution**. Time is stamped per chunk.

**Stage 1 — complete solution (~4–5 h)**
1. scaffold: solution, Core/Adapters/Api/Tests projects, Vite app, `.gitignore` (`/App_Data/` anchored)
2. core: domain, ports, use cases (+ tests with fakes, architecture test)
3. adapters: JSON repository, console notifications (+ tests)
4. api: endpoints, DTOs, validation, ProblemDetails, `/health` (+ integration tests)
5. api: JWT admin auth, rate limit, fail-closed secrets (+ tests)
6. adapters: Gemini summary (+ stub-HTTP tests)
7. frontend: tickets list, filters, desktop table + mobile cards, new ticket modal
8. frontend: details page, admin edit on the detail page, login
9. e2e: specs 1, 2, 3, 4, 5, 6
10. README v1: run, design decisions, known limitations, screenshots, link to docs/AI-ASSISTED-DEVELOPMENT.md

**Stage 2 — bonuses beyond the brief (~3–5 h)**
11. adapters: OpenAI summary (provider chosen by config)
12. adapters: SMTP notifications (MailKit)
13. image upload: store adapter + endpoint + modal field + display
14. admin inline editing in the table and mobile cards (D16 complete)
15. docker: Dockerfiles, nginx, compose
16. e2e: specs 4b, 7, 8; README: scaling section + final screenshots

An independent code review pass runs before each large commit; the auth code gets the strictest one.
A screenshot of visual work is checked before it is committed.

---

## 10. Out of scope

Database, user registration / customer accounts, multiple admin users, token revocation, pagination (dataset is
tiny; the whole file is filtered in memory — listed under Known limitations), background AI/email queues (D17),
running more than one API replica (see §11), status workflow rules, frontend component unit tests (Playwright
covers the UI), cloud deployment, i18n, real email delivery tracking.

---

## 11. Scaling (goes into README)

Today the API is **single-instance** by design of the storage. What blocks scale-out and which adapter replaces it:

| Bottleneck | Why | Scale-out replacement |
|---|---|---|
| JSON file + in-process cache + `SemaphoreSlim` | lock and cache live in one process; two containers overwrite each other | `ITicketRepository` → PostgreSQL/SQL Server adapter with optimistic concurrency (row version → 409) |
| Images on local disk | each container has its own disk | `IImageStore` → S3 / Azure Blob adapter |
| Email + AI inside the request | slow third party holds the request; a crash loses the email | outbox table + worker, or a queue (RabbitMQ/SQS) |
| In-memory login rate limiter | counted per instance | Redis-backed limiter or API gateway |

Already scale-ready: stateless JWT (shared signing key via secrets), config via environment variables, static
frontend behind nginx/CDN, `/health` for orchestrators. **Core does not change for any of these** — that is the
point of D2.

---

## 12. End-to-end verification

1. `dotnet test` and `npm run test:e2e` (in `frontend/`) — all green.
2. Dev: `dotnet run` (API :5200) + `npm run dev` (:5201) → open `http://localhost:5201`.
3. List shows the 5 seeded tickets; status "In Progress" → Jane Smith only; search "laptop" → John Doe only; URL reflects filters.
4. Create a ticket with an image as anonymous → appears at top with AI summary (or none with `Ai:Provider=None`);
   console shows the "Ticket received" email with a working `/tickets/{id}` link; open it.
5. Anonymous details page: email masked, no edit controls; `curl -X PUT` without a token → 401.
6. Log in `admin` → change status and resolution together → one email in console; save with no changes → no
   email. In the table, change two rows inline → one Save → two emails, both rows updated.
7. Restart the API → changes persisted in `App_Data/tickets.json`; `dataset.json` unchanged (`git status` clean).
8. Mobile width 375 px: cards, no horizontal scroll, modal usable, admin edits a card and saves.
9. `docker compose up --build` → `http://localhost:8080` works the same; `/health` → Healthy.
