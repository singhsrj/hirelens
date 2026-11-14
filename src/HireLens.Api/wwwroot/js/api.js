// Thin fetch wrapper for the HireLens API. Every failure becomes an ApiError with a
// human-readable message and, for validation failures, per-field errors.

export class ApiError extends Error {
  constructor(status, problem) {
    super(messageFor(status, problem));
    this.status = status;
    this.fieldErrors = problem?.errors ?? {};
  }
}

function messageFor(status, problem) {
  if (status === 0) return "Can't reach the server. Check your connection and try again.";
  if (status === 429) return "Too many attempts from this network. Wait a minute, then try again.";
  if (status === 400 && problem?.errors) return "Some fields need fixing. See the messages below.";
  if (problem?.detail) return problem.detail;
  if (status >= 500) return "The server hit an error. Try again in a moment.";
  return problem?.title ?? `Request failed (${status}).`;
}

async function request(method, url, body) {
  let response;
  try {
    response = await fetch(url, {
      method,
      headers: body === undefined ? { Accept: "application/json" } : { "Content-Type": "application/json", Accept: "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, null);
  }

  const isJson = response.headers.get("content-type")?.includes("json");
  const data = isJson ? await response.json().catch(() => null) : null;
  if (!response.ok) throw new ApiError(response.status, data);
  return data;
}

const id = (value) => encodeURIComponent(value);

export const api = {
  listJobs: (status = "open") => request("GET", `/api/jobs?status=${id(status)}`),
  getJob: (jobId) => request("GET", `/api/jobs/${id(jobId)}`),
  createJob: (job) => request("POST", "/api/jobs", job),
  updateJob: (jobId, job) => request("PUT", `/api/jobs/${id(jobId)}`, job),
  suggestSkills: (descriptionText) => request("POST", "/api/jobs/extract-skills", { descriptionText }),

  apply: (jobId, application) => request("POST", `/api/jobs/${id(jobId)}/applications`, application),
  listApplicants: (jobId) => request("GET", `/api/jobs/${id(jobId)}/applications`),
  getApplication: (appId) => request("GET", `/api/applications/${id(appId)}`),
  setStatus: (appId, status) => request("PATCH", `/api/applications/${id(appId)}/status`, { status }),
  retryNer: (appId) => request("POST", `/api/applications/${id(appId)}/retry-ner`),
};
