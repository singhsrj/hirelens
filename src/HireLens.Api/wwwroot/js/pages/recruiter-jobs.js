// Recruiter: list of jobs with filter, links to applicants and edit.
import { api } from "../api.js";
import { $, h, mountSignOut, formatDate, capitalize, loading, emptyState, errorState, showFlash } from "../ui.js";

const host = $("#jobs");
const filter = $("#filter");
let status = "open";

filter.addEventListener("click", (e) => {
  const button = e.target.closest("button[data-status]");
  if (!button || button.dataset.status === status) return;
  status = button.dataset.status;
  filter.querySelectorAll("button").forEach((b) => b.setAttribute("aria-pressed", String(b === button)));
  load();
});

async function load() {
  loading(host);
  try {
    const jobs = await api.listJobs(status);
    if (jobs.length === 0) {
      const text = status === "closed" ? "Jobs you close show up here." : "Create a job to start receiving applications.";
      emptyState(host, status === "closed" ? "No closed jobs" : "No jobs yet", text,
        status === "closed" ? null : h("a", { class: "btn btn-primary", href: "/recruiter/job.html" }, "Create job"));
      return;
    }
    host.replaceChildren(table(jobs));
  } catch (error) {
    errorState(host, error, load);
  }
}

function table(jobs) {
  return h("table", { class: "table" },
    h("thead", {}, h("tr", {},
      h("th", { scope: "col" }, "Title"),
      h("th", { scope: "col" }, "Location"),
      h("th", { scope: "col" }, "Status"),
      h("th", { scope: "col" }, "Posted"),
      h("th", { scope: "col" }, h("span", { class: "visually-hidden" }, "Actions")))),
    h("tbody", {}, jobs.map((job) => {
      const id = encodeURIComponent(job.id);
      return h("tr", {},
        h("td", {}, h("strong", {}, job.title)),
        h("td", { "data-label": "Location" }, job.location || "Not specified"),
        h("td", { "data-label": "Status" }, h("span", { class: `status status-${job.status}` }, capitalize(job.status))),
        h("td", { class: "num", "data-label": "Posted" }, formatDate(job.createdAt)),
        h("td", {}, h("div", { class: "row-actions" },
          h("a", { class: "btn btn-small btn-primary", href: `/recruiter/applicants.html?jobId=${id}` }, "Applicants"),
          h("a", { class: "btn btn-small", href: `/recruiter/job.html?id=${id}` }, "Edit"))));
    })));
}

showFlash();
mountSignOut(api.logout, api.me);
load();
