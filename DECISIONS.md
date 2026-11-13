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

## Why MongoDB rather than SQL

- **SQL** would split a resume into 5–6 tables (candidates, skills, candidate_skills, experiences, entities…) that get joined back on every read, and every change to what NER extracts needs a migration.
- **MongoDB** stores each application as one document, which is exactly how the app reads and writes it.

**Modelling**
- *Embedded:* `statusHistory` inside an application — small, bounded, always read together.
- *Referenced:* applications point to a job by `jobId`. A job can receive unlimited applications and a document is capped at 16 MB.
- *Indexes:* `{ jobId: 1, score: -1 }` serves the ranked applicant list; a multikey index on `skills` answers "who knows X?".

**Trade-off:** if the app needed multi-table transactions (payments, bookings), PostgreSQL would be the pick — and Postgres JSONB could store these documents too. Mongo wins here because the whole model is document-shaped.
