# HireLens

A small applicant screener. A recruiter posts a job, candidates apply with their resume text, a small NER service extracts skills, and the recruiter sees applicants ranked against the job's must-have skills.

> Status: **Phase 1 — foundations.** See [`docs/HireLens_Development_Plan.md`](docs/HireLens_Development_Plan.md) for the full plan and [`DECISIONS.md`](DECISIONS.md) for the reasoning behind the tech choices.

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

- App: http://localhost:5063
- API docs (Scalar): http://localhost:5063/scalar
- Health check: http://localhost:5063/health

Try the Phase 1 smoke test with `src/HireLens.Api/HireLens.Api.http`, or:

```bash
curl -X POST http://localhost:5063/api/hello -H "Content-Type: application/json" -d '{"message":"hi"}'
curl http://localhost:5063/api/hello
```

## Tests

```bash
dotnet test
```

## Project layout

```
src/HireLens.Api/      ASP.NET Core API + static frontend (wwwroot)
tests/HireLens.Tests/  xUnit tests
docs/                  development plan
docker-compose.yml     local MongoDB
```
