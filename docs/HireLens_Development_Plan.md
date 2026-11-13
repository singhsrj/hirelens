# HireLens: Development Plan

A small applicant screener for the Tescra / ACHNET Software Engineer role. A recruiter posts a job, candidates apply, a small NER service pulls skills out of their resumes, and the recruiter sees applicants ranked against the job description.

**Stack:** ASP.NET Core (.NET 10, C#) · HTML / CSS / JavaScript · MongoDB · Python (FastAPI + spaCy) for NER
**Length:** about 2 weeks, in 5 phases

---

## What the app does (and nothing more)

1. The **recruiter creates a job** by pasting in a JD. The NER service suggests the required skills, and the recruiter confirms or edits them.
2. A **candidate applies** with a name, email and pasted resume text. NER extracts their skills, and the app saves a match score.
3. The **recruiter sees applicants ranked by score**, with matched and missing skills for each, and can shortlist or reject them.

## Architecture

```
HTML / CSS / JS  (static files served by ASP.NET from wwwroot)
        │
ASP.NET Core Web API  (.NET 10, C#)
   ├── MongoDB         → jobs, applications
   └── HTTP → Python FastAPI + spaCy  (NER only)
```

## Key decisions and the reason for each

| Decision | Reason |
|---|---|
| .NET 10 | Current LTS release (supported to Nov 2028), the version a new project should start on. |
| Plain HTML/CSS/JS, served from the same app | The JD asks for exactly this. One deploy, no build step, and it shows fundamentals without a framework doing the work. |
| MongoDB instead of SQL | See the section below. |
| NER as a separate Python service | spaCy only exists in Python. If the service is down, the application is still saved with skills marked `pending`, so one failure doesn't take down the whole flow. |
| No caching layer | The data is small and MongoDB with the right index is fast enough. I'd add a cache only after measuring a real bottleneck. |
| No queues, no extra microservices | One recruiter and a few hundred applicants don't need them. |
| Score = must-have skills matched ÷ total must-have skills | Easy to explain, and the recruiter sees exactly why someone ranked where they did. |

## Why MongoDB over a SQL database

**The core entity is a resume, and resumes are irregular.** One candidate lists 4 skills and no certifications, another lists 25 skills, 3 certifications and nested project lists. The NER output is nested too, and new entity types will be added later.

- **In SQL** this becomes 5–6 tables (candidates, skills, candidate_skills, experiences, entities…). They get joined back together every time a profile is shown, and every change to the extraction needs a migration.
- **In MongoDB** one application is one document, read and written whole. That's exactly how the app uses it.

**Modelling choices:**
- **Embedded:** each application's `statusHistory`. It's small, bounded, and always read with the application.
- **Referenced:** applications point to their job by `jobId` and aren't embedded inside the job. A job can get unlimited applications, and a single document is capped at 16 MB.
- **Indexes:**
  - `{ jobId: 1, score: -1 }` serves the ranked list with one index.
  - A multikey index on `skills` answers "which candidates know X?"

**The honest trade-off:** if the app handled payments or bookings that need transactions across many tables, I'd use PostgreSQL. Postgres JSONB could also store these documents. I chose MongoDB because the whole data model is document-shaped.

## Data model

```js
// jobs
{
  _id, title, location,
  descriptionText,
  mustHaveSkills: ["C#", "MongoDB"],
  niceToHaveSkills: ["Python"],
  status: "open" | "closed",
  createdAt
}

// applications
{
  _id, jobId,
  name, email, resumeText,
  nerStatus: "done" | "pending",
  skills: ["C#", "JavaScript"],
  entities: { orgs: [], locations: [], dates: [] },
  score: 0.75,
  matched: ["C#"], missing: ["MongoDB"],
  status: "applied" | "shortlisted" | "rejected",
  statusHistory: [{ status: "applied", at: ISODate }],
  createdAt
}
```

## API endpoints

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/jobs` | Create a job |
| GET | `/api/jobs` | List open jobs |
| GET | `/api/jobs/{id}` | Job details |
| PUT | `/api/jobs/{id}` | Edit a job / close it |
| POST | `/api/jobs/extract-skills` | Suggest skills from JD text (calls NER) |
| POST | `/api/jobs/{id}/applications` | Candidate applies |
| GET | `/api/jobs/{id}/applications` | Applicants ranked by score |
| PATCH | `/api/applications/{id}/status` | Shortlist or reject |
| POST | `/api/applications/{id}/retry-ner` | Re-run NER on a `pending` application |

NER service (Python): `POST /extract`, which takes `{ text }` and returns `{ skills, orgs, locations, dates }`.

---

## Development plan: 5 phases

### Phase 1: Foundations and planning (Days 1–3)
**Goal:** learn enough C# and ASP.NET Core to build, and set up the project.
- Learn the C# basics: classes, async/await, LINQ, dependency injection.
- Follow Microsoft's tutorial "Create a web API with ASP.NET Core and MongoDB".
- Create the repo, solution structure and a GitHub Projects board with one issue per feature.
- Write `DECISIONS.md` (the table above, in your own words) and a first README with scope and the out-of-scope list.
- Add a `docker-compose.yml` that runs MongoDB locally.

**Done when:** a "hello" endpoint reads from and writes to MongoDB.

### Phase 2: Jobs and applications backend (Days 4–6)
**Goal:** the core API works without NER.
- Add the `Job` and `Application` models and a repository per collection, using the official MongoDB C# driver.
- Build the job CRUD endpoints and the apply endpoint, with skills entered by hand for now.
- Add the scoring logic and save `score`, `matched` and `missing` when an application is created.
- Add the ranked-applicants endpoint and the status change (shortlist / reject, appended to `statusHistory`).
- Create the indexes on startup: `{ jobId: 1, score: -1 }` and `skills`.
- Add input validation and ASP.NET Core's built-in rate limiter on the apply endpoint.
- Write xUnit tests for scoring and status changes.

**Done when:** the whole flow works through the OpenAPI / Scalar UI.

### Phase 3: Frontend (Days 7–9)
**Goal:** a usable UI in plain HTML, CSS and JavaScript.
- **Public job list** and a **job detail page** with the application form.
- A **recruiter page** to create and edit jobs.
- A **recruiter applicants view**: a ranked table with matched and missing skills as tags, plus shortlist and reject buttons.
- Use `fetch` for API calls, show loading and error states, and keep the layout responsive.

**Done when:** a recruiter and a candidate can complete the flow in the browser.

### Phase 4: NER service and integration (Days 10–12)
**Goal:** skills are extracted automatically.
- Build a Python FastAPI service with spaCy `en_core_web_sm` and an EntityRuler holding a skill list (custom `SKILL` label). Use the built-in ORG, GPE and DATE labels for organisations, locations and dates.
- Call it from .NET with a typed `HttpClient` and a short timeout.
- If the call fails, save the application with `nerStatus: "pending"` and add the retry endpoint.
- Hook up "Suggest skills" on the create-job form.
- Write a few pytest tests for the extractor, and an xUnit test for the fallback path.

**Done when:** pasting a JD suggests skills, and stopping the NER service doesn't break applying.

### Phase 5: Ship and document (Days 13–14)
**Goal:** live, documented and easy to explain.
- Dockerise both services and finish `docker-compose.yml` (API + NER + MongoDB).
- Deploy with MongoDB Atlas (free tier) and a host for the two containers.
- Seed demo data: 2 jobs and about 15 applicants.
- **Finish the README:**
  - architecture diagram
  - setup steps
  - screenshots
  - decisions
  - out-of-scope list
  - what you'd do next (auth, PDF parsing, caching once measured)
- Close the GitHub issues, tag `v1.0`, and write the resume bullet from real numbers (for example, ranked-list query time with and without the index).

**Done when:** a live link and repo are ready to share with the recruiter.

---

## Deliberately not building

- User accounts beyond one seeded recruiter login
- PDF parsing (pasted text only)
- A caching layer
- React or another frontend framework
- Message queues, Kubernetes
- LLM-based scoring

## How this maps to the JD

| JD asks for | Where it shows |
|---|---|
| Microsoft technologies | ASP.NET Core Web API in C# |
| HTML, CSS, JavaScript from design specs | Phase 3 frontend |
| MongoDB / NoSQL | Data model, indexes, the SQL vs NoSQL argument |
| Python, NER | Phase 4 NER service |
| Data structures, OOP | Models, repositories, scoring logic |
| Entire lifecycle, agile | GitHub Projects board, phases, tests, deploy, docs |
| Performance, security | Indexes, input validation, rate limiting |
