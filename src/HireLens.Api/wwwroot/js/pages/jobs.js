// Public list of open roles.
import { api } from "../api.js";
import { $, h, chips, formatDate, loading, emptyState, errorState } from "../ui.js";

const host = $("#jobs");

async function load() {
  loading(host);
  try {
    const jobs = await api.listJobs("open");
    if (jobs.length === 0) {
      emptyState(host, "No open roles right now", "Check back soon. New roles are posted here first.");
      return;
    }
    host.replaceChildren(h("ul", { class: "job-list" }, jobs.map(row)));
  } catch (error) {
    errorState(host, error, load);
  }
}

function row(job) {
  const href = `/job.html?id=${encodeURIComponent(job.id)}`;
  return h("li", { class: "job-row" },
    h("div", {},
      h("h2", {}, h("a", { href }, job.title)),
      h("p", { class: "meta" }, [job.location || "Location not specified", `Posted ${formatDate(job.createdAt)}`].join(", ")),
      chips(job.mustHaveSkills)),
    h("a", { class: "btn", href }, "View and apply"));
}

load();
