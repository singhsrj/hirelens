# HireLens

A small applicant screener. A recruiter posts a job, candidates apply with their resume text, a small NER service extracts skills, and the recruiter sees applicants ranked against the job's must-have skills.

> Status: **Phase 3 — frontend done.** Recruiter and candidate flows work in the browser (skills entered by hand; NER arrives in Phase 4). See [`docs/HireLens_Development_Plan.md`](docs/HireLens_Development_Plan.md) for the full plan and [`DECISIONS.md`](DECISIONS.md) for the reasoning behind the tech choices.

## Stack

- **API:** ASP.NET Core (.NET 10, C#), Minimal APIs, MongoDB C# driver
- **Frontend:** plain HTML / CSS / JavaScript served from `wwwroot`
- **Database:** MongoDB
- **NER:** Python, FastAPI + spaCy *(Phase 4)*

```
HTML / CSS / JS  (static files served by ASP.NET from wwwroot)
        │
ASP.NET Core Web API  (.NET 10, C#)
   ├── MongoDB         → jobs, applications
   └── HTTP → Python FastAPI + spaCy  (NER only)
```

## Scope

1. Recruiter creates a job from a pasted JD; NER suggests the required skills, recruiter confirms/edits.
2. Candidate applies with name, email and pasted resume text; NER extracts skills; a match score is saved.
3. Recruiter sees applicants ranked by score with matched/missing skills, and can shortlist or reject.

### Out of scope

- User accounts beyond one seeded recruiter login
- PDF parsing (pasted text only)
- A caching layer
- React or another frontend framework
- Message queues, Kubernetes
- LLM-based scoring

## Running locally

Prerequisites: .NET 10 SDK, Docker Desktop.

```bash
docker compose up -d mongo          # MongoDB on localhost:27017
dotnet run --project src/HireLens.Api
```

- Candidate site: http://localhost:5063 (open roles, apply)
- Recruiter: http://localhost:5063/recruiter/ (create/edit jobs, ranked applicants, shortlist/reject)
- API docs (Scalar): http://localhost:5063/scalar
- Health check: http://localhost:5063/health

Try the requests in `src/HireLens.Api/HireLens.Api.http` (VS Code REST Client / Visual Studio), or use the Scalar UI.

## Pages

| Page | Who | What it does |
|---|---|---|
| `/` | Candidate | Open roles with their must-have skills |
| `/job.html?id=…` | Candidate | Role details and the application form |
| `/recruiter/` | Recruiter | Jobs, filterable by open / closed / all |
| `/recruiter/job.html[?id=…]` | Recruiter | Create or edit a job, close or reopen it |
| `/recruiter/applicants.html?jobId=…` | Recruiter | Applicants ranked by score. Each row shows a strip with one segment per must-have skill (filled = has it), the matched and missing skills, shortlist / reject, and the resume on demand |

Plain HTML, CSS and ES modules in `wwwroot/`, no build step. Recruiter pages have no login yet (the single seeded recruiter login is planned).

## API

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/jobs` | Create a job |
| GET | `/api/jobs?status=open\|closed\|all` | List jobs (open by default) |
| GET | `/api/jobs/{id}` | Job details |
| PUT | `/api/jobs/{id}` | Edit a job / close or reopen it (rescores applicants if must-haves change) |
| POST | `/api/jobs/{jobId}/applications` | Candidate applies (rate limited: 5/min per IP) |
| GET | `/api/jobs/{jobId}/applications` | Applicants ranked by score |
| GET | `/api/applications/{id}` | Full application incl. resume text and status history |
| PATCH | `/api/applications/{id}/status` | Shortlist or reject |

Coming in Phase 4: `POST /api/jobs/extract-skills`, `POST /api/applications/{id}/retry-ner`.

Errors are returned as [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457): 400 validation, 404 not found, 409 duplicate application or closed job, 429 rate limited.

## Tests

```bash
dotnet test        # xUnit: scoring, status rules, services (no database needed)
```

**End-to-end browser test** (`tests/e2e/`): drives the recruiter and candidate flows in the installed Microsoft Edge and saves screenshots to `tests/e2e/shots/`. It submits several applications, so start the API with a relaxed rate limit:

```bash
RateLimiting__Apply__PermitLimit=100 dotnet run --project src/HireLens.Api
# PowerShell: $env:RateLimiting__Apply__PermitLimit=100; dotnet run --project src/HireLens.Api
cd tests/e2e && npm install && npm test        # BASE_URL=... to target another host
```

Each run creates its own uniquely titled job, so it works against a database that already has data.

## Project layout

```
src/HireLens.Api/
  Domain/              Job, JobApplication, scoring, skill normalisation (no Mongo/HTTP logic)
  Data/                Mongo setup, repositories, startup index creation
  Features/            Jobs and Applications: request/response contracts, services, endpoints
  Common/              Result type -> ProblemDetails
  wwwroot/             static frontend: css/app.css, js/api.js (fetch wrapper), js/ui.js (DOM helpers), js/pages/*.js
tests/HireLens.Tests/  xUnit tests
tests/e2e/             browser end-to-end test (Node + Edge)
docs/                  development plan
docker-compose.yml     local MongoDB
```
