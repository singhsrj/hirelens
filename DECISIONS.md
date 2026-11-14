# Decisions

A running log of the technical choices in HireLens and why each was made. New decisions get appended; reversed ones get struck through with a note, not deleted.

| # | Decision | Why |
|---|---|---|
| 1 | **.NET 10 (LTS)** for the API | Supported until Nov 2028; a new project should start on the current LTS. |
| 2 | **Plain HTML / CSS / JS**, served from the API's `wwwroot` | The role asks for these fundamentals directly. One deployable, no frontend build step. |
| 3 | **MongoDB** as the database | A resume and its NER output are irregular, nested documents. One application = one document, read and written whole. See below. |
| 4 | **NER in a separate Python service** (FastAPI + spaCy) | spaCy is Python-only. If NER is down, the application is still saved with `nerStatus: "pending"` and can be retried, so one failure doesn't break applying. |
| 5 | **No cache** | Small data; Mongo with the right index is fast enough. A cache gets added only after a measured bottleneck. |
| 6 | **No queues or extra services** | One recruiter and a few hundred applicants don't need them. |
| 7 | **Score = must-have skills matched ÷ total must-have skills** | Simple, and explainable: the recruiter sees exactly which skills matched and which are missing. |
| 8 | **Minimal APIs** grouped per feature (`Endpoints/*.cs`) instead of MVC controllers | Less ceremony for a small API, first-class OpenAPI support in .NET 10. |
| 9 | **Singleton `IMongoClient`** registered in DI | The driver's client is thread-safe and owns the connection pool; creating one per request would waste connections. |
| 10 | **camelCase BSON convention + ignore extra elements** | Documents in Mongo match the JSON the API returns, and old documents still deserialise as fields are added. |
| 11 | **Ranked-list index is `{ jobId: 1, score: -1, createdAt: 1 }`** (plan said `{ jobId, score }`) | Ties on score are broken by who applied first. Adding `createdAt` to the index means Mongo still walks the index in sort order instead of doing an in-memory sort. |
| 12 | **Unique index on `{ jobId, email }`**, emails lower-cased | One application per person per job, enforced by the database rather than a racy "check then insert". Duplicates return 409. |
| 13 | **Editing a job's must-have skills rescores its existing applications** (one unordered bulk write) | Otherwise the ranked list would quietly show scores computed against the old requirements. A few hundred updates is cheap. |
| 14 | **Status rules:** `applied → shortlisted/rejected`, shortlisted ⇄ rejected allowed, never back to `applied`; repeating the current status is a no-op | Recruiters change their minds, but "un-applying" makes no sense. Idempotent repeats make double-clicks and retries safe. Status + history are written in one atomic `$set` + `$push`. |
| 15 | **Expected failures are a `Result<T>` value, not exceptions** | Not found / conflict / invalid are normal outcomes. Each maps to a ProblemDetails response (404 / 409 / 400); exceptions are kept for things that are actually broken. |
| 16 | **.NET 10 built-in validation** (`AddValidation()` + DataAnnotations / `IValidatableObject`) | No extra library. Note: DataAnnotations runs `IValidatableObject` rules only after attribute rules pass, and error keys are PascalCase (`Title`), not camelCase. |
| 17 | **Per-IP fixed-window rate limit on apply** (5/min, configurable under `RateLimiting:Apply`) | It's the only public write endpoint. Behind a reverse proxy this needs forwarded headers (Phase 5) or every user shares one IP. |
| 18 | **`TimeProvider` injected** instead of `DateTime.UtcNow` | Tests control the clock, e.g. to check tie-breaking by apply time and status-history timestamps. |
| 19 | **Native ES modules, one script per page, no bundler** | Browsers load `import` natively; a build step would add tooling without benefit at this size. Shared code lives in `api.js` and `ui.js`. |
| 20 | **User data only ever reaches the DOM as text** (`h()` helper, `textContent`; no `innerHTML`) | Resumes and names are untrusted input. Building nodes instead of HTML strings makes XSS structurally impossible rather than relying on escaping. |
| 21 | **Client validation mirrors the API rules; the API stays the authority** | Instant feedback without a round trip, but the server re-validates everything. API field errors are mapped onto the form case-insensitively (the API returns `Title`, the form uses `title`). |
| 22 | **The score is drawn as a strip with one segment per must-have skill** | It's the plan's "the recruiter sees exactly why someone ranked where they did", made visual: a 67% is visibly "2 of 3, missing MongoDB". |
| 23 | **Resume text is fetched only when a recruiter opens it** | Keeps the ranked list payload small (the list endpoint already excludes `resumeText`). |

## Why MongoDB rather than SQL

- **SQL** would split a resume into 5–6 tables (candidates, skills, candidate_skills, experiences, entities…) that get joined back on every read, and every change to what NER extracts needs a migration.
- **MongoDB** stores each application as one document, which is exactly how the app reads and writes it.

**Modelling**
- *Embedded:* `statusHistory` inside an application — small, bounded, always read together.
- *Referenced:* applications point to a job by `jobId`. A job can receive unlimited applications and a document is capped at 16 MB.
- *Indexes:* `{ jobId: 1, score: -1, createdAt: 1 }` serves the ranked applicant list; a multikey index on `skills` answers "who knows X?"; a unique `{ jobId, email }` blocks duplicate applications.

**Trade-off:** if the app needed multi-table transactions (payments, bookings), PostgreSQL would be the pick — and Postgres JSONB could store these documents too. Mongo wins here because the whole model is document-shaped.
