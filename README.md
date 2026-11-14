# HireLens

A small applicant screener. A recruiter posts a job, candidates apply with their resume text, a small NER service extracts skills, and the recruiter sees applicants ranked against the job's must-have skills.

> Status: **Phase 4 — NER integrated.** Skills are extracted from resumes and job descriptions by a Python spaCy service; if it's down, applying still works and extraction can be retried. See [`docs/HireLens_Development_Plan.md`](docs/HireLens_Development_Plan.md) for the full plan and [`DECISIONS.md`](DECISIONS.md) for the reasoning behind the tech choices.

## Stack

- **API:** ASP.NET Core (.NET 10, C#), Minimal APIs, MongoDB C# driver
- **Frontend:** plain HTML / CSS / JavaScript served from `wwwroot`
- **Database:** MongoDB
- **NER:** Python 3.12, FastAPI + spaCy (`en_core_web_sm` + an EntityRuler skill list), in `ner/`

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

Prerequisites: .NET 10 SDK, Docker Desktop. (The first `docker compose up` builds the NER image, which takes a few minutes.)

```bash
docker compose up -d                # MongoDB on :27017 and the NER service on :8000
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

| POST | `/api/jobs/extract-skills` | Suggest skills from a job description (NER; 20/min per IP) |
| POST | `/api/applications/{id}/retry-ner` | Re-run skill extraction on a `pending` application |

Errors are returned as [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457): 400 validation, 404 not found, 409 duplicate application / closed job / already extracted, 429 rate limited, 503 NER service unavailable.

### NER service

`POST http://localhost:8000/extract` with `{ "text": "..." }` returns `{ skills, orgs, locations, dates }`.

- **Skills** come from an EntityRuler (custom `SKILL` label) built from [`ner/app/skills.json`](ner/app/skills.json): canonical names plus aliases (`js` → JavaScript, `csharp` → C#). Ambiguous short names (Go, R, C) only match with exact capitalisation. Add a skill by editing that file and rebuilding the image.
- **Organisations, places, dates** come from spaCy's built-in ORG, GPE/LOC and DATE labels. The small model is noisy here (e.g. "Software" in "Software engineer" can be tagged ORG); these fields are shown to the recruiter but never affect the score.
- **If the service is down**, the API saves the application with `nerStatus: "pending"` and a score of 0 and logs a warning; the recruiter sees "Not scored" with a "Read skills now" button. A stopped service costs the candidate about 1 s (connect timeout), not an error.

## Tests

```bash
dotnet test        # xUnit: scoring, status rules, services, NER client + fallback (no database or NER needed)

# NER service tests (pytest, run in Docker so no local Python setup is needed)
docker build --target test -t hirelens-ner-test ner && docker run --rm hirelens-ner-test
```

**End-to-end browser test** (`tests/e2e/`): needs `docker compose up -d` (MongoDB + NER). Drives the recruiter and candidate flows in the installed Microsoft Edge and saves screenshots to `tests/e2e/shots/`. It submits several applications, so start the API with a relaxed rate limit:

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
  Ner/                 typed HttpClient for the NER service (returns null on any failure)
  Common/              Result type -> ProblemDetails
  wwwroot/             static frontend: css/app.css, js/api.js (fetch wrapper), js/ui.js (DOM helpers), js/pages/*.js
tests/HireLens.Tests/  xUnit tests
tests/e2e/             browser end-to-end test (Node + Edge)
docs/                  development plan
ner/                   Python NER service: app/ (FastAPI + spaCy), tests/ (pytest), Dockerfile
docker-compose.yml     local MongoDB + NER service
```
