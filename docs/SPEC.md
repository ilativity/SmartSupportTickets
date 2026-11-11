# SPEC — Smart Customer Support System

Source assignment: [docs/assignment/ASSIGNMENT.md](assignment/ASSIGNMENT.md)
(upstream: https://github.com/oringilad/Dotnet_React_EX). Status: **draft, awaiting approval** (2026-11-11).

Repository: **fork** of the upstream, renamed `ilativity/SmartSupportTickets` — the history shows the
solution is built on the provided skeleton (upstream contained only the task, dataset, images and an empty
`.slnx`). The upstream README moved to `docs/assignment/`; the root README describes the solution.

The build follows this file. Every decision here is repeated with its reason in the final `README.md`
("Design decisions" section) — the reviewer must be able to read *why*, not only *what*.

---

## 1. Decisions already taken

| # | Decision | Reason |
|---|----------|--------|
| D1 | Backend targets **net9.0**, ASP.NET Core **Minimal API** | Assignment asks for .NET 8/9 and prefers Minimal API. .NET 9 left support 2026-11-10 → README names .NET 10 LTS as the next step. |
| D2 | **One API project + one test project**, folders instead of layers-as-projects | "Explain every line" — keep it small. Separation is by folder + interfaces: Endpoints → Services → Persistence / Infrastructure. |
| D3 | Anyone may **list, view, create**; only **Admin (JWT)** may **edit** | Resolves the README conflict ("customers view and edit" vs bonus "only logged users can edit"). |
| D4 | Public responses **never expose a full email**: the list omits it, details mask it (`j***@example.com`) unless the caller is Admin | The list is public and lists every ticket id, so "knowing the id" is not a secret. |
| D5 | JSON file storage behind `ITicketRepository`, **in-memory cache + `SemaphoreSlim` + atomic write** (temp file → replace) | Concurrent requests must not corrupt the file; swap to a DB = new repository class only. |
| D6 | Runtime data lives in `App_Data/` (git-ignored), **seeded from the repo's `dataset.json`** on first run | Running the app never dirties the committed dataset; reset = delete `App_Data`. |
| D7 | AI summary: `ISummaryService` with **Gemini** and **OpenAI** implementations + `None`, chosen by `Ai:Provider` | Both providers named in the assignment; shows the abstraction. No key → ticket still created, `summary = null`. |
| D8 | Email: `IEmailService` with **Console** (default) and **Smtp** (MailKit, Gmail App Password), chosen by `Email:Provider` | Mock is required, real SMTP is a bonus. Reviewer runs it with zero setup. |
| D9 | **AI/email failures never fail the request** — logged, request succeeds | A ticket is the customer's data; a third-party outage must not lose it. |
| D10 | Status + resolution changed in one save → **one** email listing both changes | Assignment lists 3 triggers; two emails for one click is noise. No change → no email. |
| D11 | Optional **image upload** on create (dataset has `imageUrl`, mockup has "Upload an image") | Hidden requirement in the provided data. |
| D12 | Frontend: **Vite + React + TypeScript + Tailwind**, React Router, TanStack Query | Typed API contract, cached server state, URL-addressable ticket view `/tickets/:id`. |
| D13 | Secrets only via `dotnet user-secrets` / env vars; Development has safe local defaults | Public repo. |
| D14 | Code comments: XML doc on public types/members + comments that explain **why** (not what) | Every line must be explainable; comments carry the reasoning. |
| D15 | Update = **read-modify-write inside the repository lock**; the repository returns the `(before, after)` pair and emails are derived from it | Reading state outside the lock lets two concurrent admin edits race and send wrong/duplicate emails. |
| D16 | Admin edits **both** inline in the tickets table (as in `ticket-manage.png`: status dropdown + resolution, one Save) **and** on `/tickets/:id` | The mockup shows inline management; the brief requires editing on the detail view. Both call the same `PUT`. |
| D17 | AI summary is generated **synchronously** during create (10 s budget), not by a background queue | The brief says "while saving the ticket, call an AI API to generate and display a short summary"; synchronous is simpler to explain. Cost: Submit waits ~1–3 s, shown with a spinner. |
| D18 | `GET /api/tickets/statuses` serves the status list | The frontend never hard-codes `"In Progress"`; one source of truth. |

---

## 2. Repository layout

```
SmartSupportTickets/
├─ MX_EX.slnx                         # provided solution file; both .NET projects added to it
├─ dataset.json                       # provided seed data (never written at runtime)
├─ README.md                          # how to run + all design decisions + screenshots
├─ docs/  SPEC.md · assignment/
├─ backend/
│  ├─ SupportTickets.Api/
│  │  ├─ Program.cs                   # composition root only: DI, auth, CORS, middleware, MapXxx()
│  │  ├─ Endpoints/   TicketEndpoints.cs · AuthEndpoints.cs · UploadsEndpoints (static files)
│  │  ├─ Entities/    Ticket.cs · TicketStatus.cs
│  │  ├─ Dtos/        TicketListItemDto · TicketDetailsDto · CreateTicketRequest · UpdateTicketRequest
│  │  │               · TicketQuery · LoginRequest · LoginResponse
│  │  ├─ Mapping/     TicketMappings.cs (entity → DTO, email masking)
│  │  ├─ Services/    ITicketService · TicketService · TicketNotificationComposer (email texts)
│  │  ├─ Persistence/ ITicketRepository · JsonTicketRepository · JsonFileStorageOptions
│  │  ├─ Infrastructure/
│  │  │  ├─ Ai/       ISummaryService · GeminiSummaryService · OpenAiSummaryService · NoSummaryService · AiOptions
│  │  │  ├─ Email/    IEmailService · ConsoleEmailService · SmtpEmailService · EmailOptions
│  │  │  ├─ Auth/     JwtOptions · AdminOptions · TokenService · PasswordHashTool
│  │  │  ├─ Files/    IImageStorage · LocalImageStorage (validation by size + magic bytes)
│  │  │  └─ Validation/ ValidationFilter<T> (DataAnnotations for minimal APIs)
│  │  ├─ appsettings.json · appsettings.Development.json
│  │  └─ App_Data/  (git-ignored: tickets.json, uploads/)
│  └─ SupportTickets.Api.Tests/       # xUnit
└─ frontend/                          # Vite React TS
   └─ src/
      ├─ api/        client.ts (fetch wrapper, auth header, ProblemDetails → Error) · tickets.ts · auth.ts · types.ts
      ├─ auth/       AuthContext.tsx · RequireAdmin
      ├─ pages/      TicketsPage · TicketDetailsPage · LoginPage · NotFoundPage
      ├─ components/ TicketTable · TicketFilters · NewTicketModal · StatusBadge · Header · Toast · EmptyState
      └─ hooks/      useDebouncedValue
```

---

## 3. Domain

`Ticket` (matches `dataset.json` field names, camelCase on disk):

| Field | Type | Notes |
|---|---|---|
| id | `Guid` | generated server-side |
| name | `string` | 2–100 chars |
| email | `string` | valid address, ≤ 254 |
| description | `string` | 10–2000 chars |
| summary | `string?` | AI output, ≤ 200 chars, null if unavailable |
| imageUrl | `string?` | `uploads/{guid}.{ext}`; seed values point to files that don't exist → UI hides a broken image |
| status | `TicketStatus` | `New`, `In Progress`, `Resolved`, `Closed` — `[JsonStringEnumMemberName("In Progress")]` (.NET 9) |
| resolution | `string` | ≤ 2000, `""` when empty (as in dataset) |
| createdAt / updatedAt | `DateTimeOffset` | UTC, ISO-8601 |

No status-transition rules (any → any): the assignment doesn't define a workflow; noted as a possible extension.

---

## 4. API (`/api` prefix, JSON, errors = RFC 7807 ProblemDetails)

| Method | Route | Auth | Body / query | Success | Errors |
|---|---|---|---|---|---|
| GET | `/api/tickets` | — | `?status=In Progress&search=laptop` (search: name or description, case-insensitive) | 200 `TicketListItemDto[]`, newest first | 400 bad status |
| GET | `/api/tickets/statuses` | — | — | 200 `string[]` (display names, in workflow order) | |
| GET | `/api/tickets/{id:guid}` | optional | — | 200 `TicketDetailsDto` (email masked unless Admin) | 404 |
| POST | `/api/tickets` | — | `multipart/form-data`: name, email, description, image? | 201 + `Location` + `TicketDetailsDto` | 400 validation, 413 too large |
| PUT | `/api/tickets/{id:guid}` | **Admin** | JSON `{ status, resolution }` | 200 `TicketDetailsDto` | 400, 401, 403, 404 |
| POST | `/api/auth/login` | — (rate-limited 5/min/IP) | `{ username, password }` | 200 `{ token, expiresAt, username }` | 401, 429 |
| GET | `/uploads/{file}` | — | — | image | 404 |
| GET | `/openapi/v1.json` (+ Scalar UI in Development) | — | | | |

DTOs:
- `TicketListItemDto`: id, name, description, summary, status, resolution, hasImage, createdAt, updatedAt — **no email** (D4).
- `TicketDetailsDto`: all fields; `email` masked for non-admin.

Implementation notes:
- Endpoints grouped with `MapGroup("/api/tickets")`, `TypedResults`, thin: bind → call service → map result.
- Validation: DataAnnotations on request records, run by `ValidationFilter<T>` → `TypedResults.ValidationProblem` (net9 has no built-in minimal-API validation).
- `POST` uses `[FromForm]` → must call `.DisableAntiforgery()`: the API uses bearer tokens, not cookies, so CSRF does not apply. Comment this in code.
- Image: jpg/png/webp, ≤ 5 MB, checked by **magic bytes** not just extension; saved as a new GUID filename (never the client's name).

---

## 5. Services and flows

**Create:** validate → store image (if any) → `ISummaryService.SummarizeAsync(description)` with 10 s timeout, exception → log + null → `repository.Add` → email "Ticket received" with tracking link `{App:FrontendBaseUrl}/tickets/{id}` → 201.

**Update (Admin):** `repository.UpdateAsync(id, apply)` runs entirely under the lock (D15): load (404) → apply
new status/resolution → nothing changed → keep as-is → else set `updatedAt = now` and write the file → return
`(before, after)`. Outside the lock the service compares the pair: no change → no email; otherwise one email
listing what changed (D10) → 200. If the file write fails, the in-memory cache is left untouched.

**AI prompt:** fixed system instruction ("summarize the support issue in one sentence, max 15 words, same language as the input, output only the summary"); the customer text is passed as a separate, delimited user message and treated as data; output trimmed; an answer far over the limit (> 40 words) is discarded as a bad summary (→ null), otherwise
capped at 200 chars. Models configurable (`Ai:Gemini:Model`, `Ai:OpenAI:Model`); defaults are the cheapest
fast model of each provider — a one-line summary does not need a large model. Typed `HttpClient`s, plain REST — no SDK, so every line is explainable.

**Email:** `EmailMessage(to, subject, textBody, htmlBody)`. `ConsoleEmailService` logs it and keeps it in an in-memory outbox (used by tests). `SmtpEmailService` = MailKit, `smtp.gmail.com:587` StartTLS, credentials from user-secrets.

**Persistence:** singleton `JsonTicketRepository`: lazy load once, all reads/writes under `SemaphoreSlim(1,1)`, write = serialize to `tickets.json.tmp` → `File.Move(overwrite: true)`. Returns copies so callers can't mutate the cache.

**Auth:** `Auth:Admin:Username` + `Auth:Admin:PasswordHash` (ASP.NET `PasswordHasher` format); `dotnet run -- hash-password <pwd>` prints a hash. Login always runs the password-hash verification, even for an unknown username (against a fixed dummy hash),
so response time does not reveal whether the username exists.
JWT HS256, `Jwt:Issuer/Audience/SigningKey`, 60 min expiry, `role=Admin`, policy `AdminOnly`. Development defaults: `admin` / `Admin123!` (documented in README; production must override).

**Cross-cutting:** `AddProblemDetails` + `UseExceptionHandler`; CORS for `App:FrontendBaseUrl`; built-in rate limiter on login; structured logging.

---

## 6. Frontend

- `/` → `/tickets`. **TicketsPage:** status dropdown + search (debounced 300 ms), both mirrored into the URL query; table on ≥ md, cards on mobile; columns Name · Summary · Description (clamped, expandable) · Status badge · Updated.
  Visitors: the whole row is one link to `/tickets/:id` (a real `<a>` stretched over the row, so it works with
  keyboard and middle-click). **Admin (D16):** Status and Resolution cells become a dropdown and a text field,
  changed rows are highlighted, one **Save changes** button sends a `PUT` per changed row and reports per-row
  failures; the name cell stays a link to the detail page. Status options come from `/api/tickets/statuses`.
  Load errors show a message with a **Retry** button; "New ticket" button → `NewTicketModal` (native `<dialog>`, Esc closes, focus returns; field-level errors from ProblemDetails; image preview; success toast with link to the new ticket).
- **TicketDetailsPage** `/tickets/:id`: id (copy button), customer, description, AI summary, image, status, resolution, dates. Admin: status select + resolution textarea + Save (disabled until dirty; success/error toast). Non-admin: read-only + "Log in to edit". 404 → friendly not-found.
- **LoginPage** `/login`: returns to the previous page after login. Header shows Login / "admin · Logout".
- Token in `sessionStorage` (cleared with the tab), attached by `api/client.ts`; a 401 logs out.
- Dev: Vite proxy `/api` and `/uploads` → backend, so no CORS needed locally.
- Tailwind, own palette defined as tokens; accessible labels, focus rings, colour contrast AA, keyboard-usable table rows.

---

## 7. Tests (`SupportTickets.Api.Tests`, xUnit)

- Repository: seed load; **`dataset.json` load → save round-trip keeps every field, status spelling and date
  format**; 20 parallel `Add`s → file valid with all 20; 2 concurrent updates of one ticket → both applied, each
  sees the correct `before` (D15).
- Auth: valid login → token with Admin role; wrong password and unknown user → 401.
- TicketService: create sends 1 email with tracking link; status-only / resolution-only / both → exactly 1 email; no change → 0; AI throws → ticket saved with null summary.
- Integration (`WebApplicationFactory`, temp `App_Data`): anon POST 201; anon PUT 401; admin PUT 200; filter by status + search; unknown id 404; list has no email; invalid body 400 with field errors.

**Frontend end-to-end — Playwright** (`frontend/e2e/`, Chromium):
- Playwright's `webServer` starts the real backend (`ASPNETCORE_ENVIRONMENT=E2E`: fresh temp data dir seeded
  from `dataset.json`, `Ai:Provider=None`, `Email:Provider=Console`) and the Vite dev server — tests hit the
  real API, nothing is mocked.
- Specs: (1) list shows seed tickets, status filter + search narrow it and sync to the URL; (2) anonymous
  user creates a ticket via the modal (incl. validation errors) and lands on its `/tickets/:id`;
  (3) anonymous details page shows a masked email and no edit controls; (4) admin logs in, changes status +
  resolution on the detail page, saves, the change survives a reload; (4b) admin edits two rows inline in the
table and saves both with one click; (5) wrong password shows an error; (6) unknown id → not-found
  page; (7) mobile viewport 375 px — no horizontal scroll, modal usable.
- Test count principle: **reasonable, not exhaustive** — every required flow and every rule with a reason in §1
  has at least one test; no tests for framework behaviour or trivial getters.

---

## 8. Commit plan (one topic per commit)

1. scaffold: solution, API + tests projects, Vite app, gitignore `App_Data`
2. backend: entities, DTOs, JSON repository (+ tests)
3. backend: ticket service, endpoints, console email (+ tests)
4. backend: JWT admin auth (+ tests)
5. backend: AI summary providers
6. backend: SMTP email, image upload
7. frontend: tickets list + filters + new ticket modal
8. frontend: ticket details + admin edit (detail page + inline table) + login
9. frontend: Playwright e2e suite
10. README: run instructions, design decisions (this §1 with reasons), **Known limitations**, screenshots

An independent code review pass runs before each large commit; the auth code gets the strictest one.

---

## 9. Out of scope

Database, user registration / customer accounts, multiple admin users, token revocation, pagination (dataset is
tiny; the whole file is filtered in memory — listed under Known limitations), background AI/email queues (D17), status workflow rules, frontend component unit tests (Playwright e2e covers the UI), Docker, deployment, i18n, real email delivery tracking.

---

## 10. End-to-end verification

1. `dotnet test` and `npm run test:e2e` (in `frontend/`) — all green.
2. `dotnet run` (backend) + `npm run dev` (frontend); open `http://localhost:5173`.
3. List shows the 5 seeded tickets; status filter "In Progress" → Jane Smith only; search "laptop" → John Doe only; URL reflects filters.
4. Create a ticket with an image as anonymous → appears at top with AI summary (or none if `Ai:Provider=None`); console shows the "Ticket received" email with a working `/tickets/{id}` link; open it.
5. Anonymous details page: email masked, no edit controls; direct `PUT` with curl → 401.
6. Log in `admin` → change status and resolution together → one email in console; save with no changes → no email.
   In the table, change two rows inline → one Save → two emails, both rows updated.
7. Restart backend → changes persisted in `App_Data/tickets.json`; `dataset.json` unchanged (`git status` clean).
8. Mobile width 375 px: no horizontal scroll, cards layout, modal usable.
