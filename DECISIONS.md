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
| 24 | **EntityRuler placed *before* spaCy's statistical NER**, with `overwrite_ents` | Known skills are claimed as `SKILL` first, so "MongoDB" or "Kafka" isn't mislabelled ORG by the model. A curated list beats a trained model here: skills are a closed, known vocabulary and the list is easy to audit. |
| 25 | **Skill list as data (`skills.json`): canonical name + aliases; ambiguous names case-sensitive** | Stored skills are always the canonical spelling, so scoring compares like with like. "Go"/"R"/"C" only match capitalised, so "I go to meetups" isn't a skill. |
| 26 | **Only the NER component of `en_core_web_sm` is loaded** (parser, tagger, lemmatizer excluded) | Faster load and inference; nothing else is used. The model loads once at startup (FastAPI lifespan). |
| 27 | **NER client returns `null` on any failure** (timeout, refused, 5xx, bad JSON); caller cancellation still throws | One place decides "NER is unavailable"; services just branch on null. Swallowing the caller's own cancellation would hide aborted requests. |
| 28 | **Apply never fails because NER is down**: saved as `nerStatus: "pending"`, score 0, recruiter retries | The plan's "one failure doesn't take down the whole flow". Pending rows show "Not scored" rather than a misleading 0%. |
| 29 | **1 s connect timeout + 5 s total timeout; default address `127.0.0.1`** | Found while testing an outage on Windows: a refused connection takes ~2 s because the OS retries, and `localhost` tries IPv6 then IPv4, so a stopped service cost each candidate ~4 s. Now ~1 s. A running service still gets 5 s to extract. |
| 30 | **Retry only on `pending`, saved with a conditional update (`nerStatus == pending`)** | Matches the plan's endpoint contract (409 if already done). The condition stops two overlapping retries from both writing. |
| 31 | **Candidates no longer type their skills** (removed from the API and form) | Self-reported skill lists are easy to stuff; scoring from the resume text is the point of the NER step. |
| 32 | **NER service runs in Docker for dev and tests** (multi-stage image: `test` target runs pytest, `runtime` target runs as non-root) | One environment everywhere, no local Python/spaCy setup; Phase 5 deploys the same image. |
| 33 | **One recruiter, cookie auth, credentials from config** (email + PBKDF2 hash via ASP.NET Core Identity's `PasswordHasher`) | The plan's single seeded recruiter login, and nothing more. No user table, no Identity database; candidates never need an account. A `hash-password` command generates the hash so no plaintext password is ever stored. |
| 34 | **Cookie is HttpOnly, SameSite=Strict, 8 h sliding; APIs answer 401/403 instead of redirecting** | HttpOnly keeps it away from scripts; SameSite=Strict means cross-site requests never carry it, which covers CSRF for a same-origin JSON API without antiforgery tokens. |
| 35 | **A wrong email is checked against a dummy hash** | Wrong-email and wrong-password attempts take the same time, so response timing doesn't reveal the recruiter's email. Sign-in is also rate limited (5/min per IP). |
| 36 | **Data Protection keys stored in MongoDB** (small `IXmlRepository`) | Keys sign the auth cookie. In a container they'd live on the filesystem and vanish on every restart, signing the recruiter out; any host that restarts or sleeps containers would make that constant. |
| 37 | **Recruiter pages gated server-side, sent `Cache-Control: no-store`, and re-check `/api/auth/me` on load** | Found in testing: after sign-out the browser showed `/recruiter/` from its cache, so the server gate never ran. `no-store` makes every visit hit the gate; the session check catches pages restored from history. Other HTML is `no-cache` so updates show at once. |
| 38 | **Candidate-facing endpoints stay public; everything touching applications is recruiter-only** | Applications hold personal data (names, emails, resumes). Browsing open jobs and applying need no account. |
| 39 | **Demo data is a `seed` command reading `Seed/demo-data.json`, run through the real NER service** | Seeding is explicit (never on startup, never over existing data without `--reset`), and seeded applicants are scored exactly like real ones. All people and employers are fictional. |
| 40 | **API image: multi-stage build, runtime on `aspnet:10.0` as the non-root `app` user** | The SDK stays out of the final image (356 MB); running as non-root limits the damage of a compromise. |
| 41 | **Optional NER API key (`X-Api-Key`) and a startup warm-up ping** | Both prepare for hosts without private networking or that sleep idle services; both are no-ops locally. The key is compared with `secrets.compare_digest`. |
| 42 | **The index claim is measured, not assumed** (`tools/benchmark/ranked-query.js`) | 50k applications: median 45 ms → 12 ms, documents examined 50,000 → 500, in-memory sort removed. Numbers come from `explain("executionStats")` and timed runs, not estimates. |

## Why MongoDB rather than SQL

- **SQL** would split a resume into 5–6 tables (candidates, skills, candidate_skills, experiences, entities…) that get joined back on every read, and every change to what NER extracts needs a migration.
- **MongoDB** stores each application as one document, which is exactly how the app reads and writes it.

**Modelling**
- *Embedded:* `statusHistory` inside an application — small, bounded, always read together.
- *Referenced:* applications point to a job by `jobId`. A job can receive unlimited applications and a document is capped at 16 MB.
- *Indexes:* `{ jobId: 1, score: -1, createdAt: 1 }` serves the ranked applicant list; a multikey index on `skills` answers "who knows X?"; a unique `{ jobId, email }` blocks duplicate applications.

**Trade-off:** if the app needed multi-table transactions (payments, bookings), PostgreSQL would be the pick — and Postgres JSONB could store these documents too. Mongo wins here because the whole model is document-shaped.
