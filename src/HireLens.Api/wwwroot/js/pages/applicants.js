// Recruiter: applicants for one job, ranked by score, with shortlist / reject.
import { api } from "../api.js";
import {
  $, h, param, chips, matchChips, scoreStrip, percent, capitalize, formatDateTime,
  loading, emptyState, errorState, toast,
} from "../ui.js";

const summaryHost = $("#summary");
const listHost = $("#applicants");
const jobId = param("jobId");

const FILTERS = [
  { key: "all", label: "All" },
  { key: "applied", label: "To review" },
  { key: "shortlisted", label: "Shortlisted" },
  { key: "rejected", label: "Rejected" },
];

let job = null;
let applicants = []; // ranked order from the API; rank = index + 1
let filter = "all";

async function load() {
  if (!jobId) {
    emptyState(listHost, "No job selected", "Open a job's applicants from your job list.",
      h("a", { class: "btn", href: "/recruiter/" }, "Back to jobs"));
    return;
  }
  loading(listHost, 6);
  try {
    [job, applicants] = await Promise.all([api.getJob(jobId), api.listApplicants(jobId)]);
    document.title = `Applicants for ${job.title} | HireLens`;
    renderSummary();
    renderList();
  } catch (error) {
    if (error.status === 404) {
      emptyState(listHost, "This job doesn't exist", "It may have been deleted.",
        h("a", { class: "btn", href: "/recruiter/" }, "Back to jobs"));
    } else {
      errorState(listHost, error, load);
    }
  }
}

function renderSummary() {
  summaryHost.replaceChildren(h("section", { class: "job-summary" },
    h("div", { class: "page-head", style: "margin-bottom: 0" },
      h("div", {},
        h("h1", {}, job.title),
        h("p", { class: "lede" },
          `${applicants.length} ${applicants.length === 1 ? "applicant" : "applicants"}, ranked by must-have skills matched. `,
          job.status === "closed" ? "This job is closed to new applications." : "")),
      h("div", { class: "actions" },
        h("a", { class: "btn", href: `/recruiter/job.html?id=${encodeURIComponent(job.id)}` }, "Edit job"))),
    chips(job.mustHaveSkills)));
}

function renderList() {
  if (applicants.length === 0) {
    emptyState(listHost, "No applicants yet",
      job.status === "open" ? "Share the role page with candidates. New applicants appear here, best match first." : "This job is closed and received no applications.",
      job.status === "open" ? h("a", { class: "btn", href: `/job.html?id=${encodeURIComponent(job.id)}` }, "Open the role page") : null);
    return;
  }

  const counts = Object.fromEntries(FILTERS.map((f) => [f.key, f.key === "all" ? applicants.length : applicants.filter((a) => a.status === f.key).length]));
  const toolbar = h("div", { class: "toolbar" },
    h("div", { class: "segmented", role: "group", "aria-label": "Filter by status" },
      FILTERS.map((f) => h("button", {
        type: "button", "aria-pressed": String(filter === f.key),
        onclick: () => { filter = f.key; renderList(); },
      }, f.label, h("span", { class: "count" }, counts[f.key])))),
    pendingNotice());

  const visible = applicants
    .map((a, i) => ({ a, rank: i + 1 }))
    .filter(({ a }) => filter === "all" || a.status === filter);

  const list = visible.length
    ? h("ol", { class: "ranked" }, visible.map(({ a, rank }) => row(a, rank)))
    : h("div", { class: "state" }, h("p", {}, `No applicants are ${FILTERS.find((f) => f.key === filter).label.toLowerCase()} yet.`));

  listHost.replaceChildren(toolbar, list);
}

function row(a, rank) {
  const pending = a.nerStatus === "pending";
  const li = h("li", { class: "applicant", "aria-label": `Rank ${rank}: ${a.name}, ${pending ? "skills not read yet" : `${percent(a.score)} match`}` },
    h("span", { class: "rank" }, rank),
    h("div", { class: "who" }, h("strong", {}, a.name), h("span", {}, a.email)),
    pending ? pendingScore(a) : scoreBlock(a),
    h("span", { class: `status status-${a.status}` }, a.status === "applied" ? "To review" : capitalize(a.status)),
    h("div", { class: "decision" },
      decisionButton(a, "shortlisted", "Shortlist"),
      decisionButton(a, "rejected", "Reject")),
    details(a));
  return li;
}

function scoreBlock(a) {
  return h("div", { class: "score" },
    h("div", { class: "score-head" },
      h("span", { class: "score-value" }, percent(a.score)),
      h("span", { class: "score-of" }, `${a.matched.length} of ${job.mustHaveSkills.length} must-haves`)),
    scoreStrip(job.mustHaveSkills, a.matched),
    matchChips(job.mustHaveSkills, a.matched));
}

/** Saved while the skill extractor was down: no score yet, so say so instead of showing 0%. */
function pendingScore(a) {
  return h("div", { class: "score score-pending" },
    h("div", { class: "score-head" },
      h("span", { class: "score-value" }, "Not scored"),
      h("span", { class: "score-of" }, "Skills haven't been read from this resume yet")),
    h("div", {},
      h("button", { type: "button", class: "btn btn-small", onclick: (e) => retry([a], e.currentTarget) }, "Read skills now")));
}

function pendingNotice() {
  const pending = applicants.filter((a) => a.nerStatus === "pending");
  if (pending.length === 0) return null;
  return h("div", { class: "notice", role: "status" },
    h("p", {}, `${pending.length} ${pending.length === 1 ? "application was" : "applications were"} saved while skill reading was unavailable, so ${pending.length === 1 ? "it isn't" : "they aren't"} scored yet.`),
    pending.length > 1
      ? h("button", { type: "button", class: "btn btn-small", onclick: (e) => retry(pending, e.currentTarget) }, "Read skills for all")
      : null);
}

/** Retries NER one application at a time, re-ranks, and reports how many succeeded. */
async function retry(targets, button) {
  button.disabled = true;
  button.setAttribute("aria-busy", "true");
  button.textContent = "Reading skills…";
  let done = 0;
  let lastError = null;
  for (const a of targets) {
    try {
      const updated = await api.retryNer(a.id);
      Object.assign(a, {
        nerStatus: updated.nerStatus, skills: updated.skills, score: updated.score,
        matched: updated.matched, missing: updated.missing,
      });
      done++;
    } catch (error) {
      lastError = error;
      if (error.status === 503) break; // the service is still down; no point trying the rest
    }
  }
  // Same order as the API: score high to low, earlier applicants first on ties.
  applicants.sort((x, y) => y.score - x.score || new Date(x.createdAt) - new Date(y.createdAt));
  renderList();
  if (lastError && done === 0) toast(lastError.message);
  else if (lastError) toast(`Scored ${done} of ${targets.length}. ${lastError.message}`);
  else toast(done === 1 ? `Scored ${targets[0].name}` : `Scored ${done} applications`);
}

function decisionButton(a, status, label) {
  return h("button", {
    type: "button", class: "btn btn-small btn-decision",
    "aria-pressed": String(a.status === status), dataset: { status },
    onclick: async (e) => {
      if (a.status === status) return;
      const button = e.currentTarget;
      button.closest(".decision").querySelectorAll("button").forEach((b) => (b.disabled = true));
      try {
        const updated = await api.setStatus(a.id, status);
        a.status = updated.status;
        toast(`${status === "shortlisted" ? "Shortlisted" : "Rejected"} ${a.name}`);
        renderList();
      } catch (error) {
        toast(error.message);
        button.closest(".decision").querySelectorAll("button").forEach((b) => (b.disabled = false));
      }
    },
  }, label);
}

/** What NER found in the resume: every skill (not just must-haves), plus organisations, places and dates. */
function extracted(full) {
  if (full.nerStatus === "pending") return h("p", { class: "small muted" }, "Skills haven't been read from this resume yet.");
  const rows = [
    ["All skills found", full.skills],
    ["Organisations", full.entities.orgs],
    ["Places", full.entities.locations],
    ["Dates", full.entities.dates],
  ].filter(([, values]) => values.length);
  if (rows.length === 0) return h("p", { class: "small muted" }, "No skills or other details were found in this resume.");
  return h("dl", { class: "extracted" },
    rows.flatMap(([label, values]) => [h("dt", {}, label), h("dd", {}, values.join(", "))]));
}

/** Resume and history are only fetched when the recruiter opens them. */
function details(a) {
  const panelId = `details-${a.id}`;
  const panel = h("div", { id: panelId, hidden: true });
  const toggle = h("button", {
    type: "button", class: "disclosure", "aria-expanded": "false", "aria-controls": panelId,
    onclick: async () => {
      const open = toggle.getAttribute("aria-expanded") === "true";
      toggle.setAttribute("aria-expanded", String(!open));
      toggle.textContent = open ? "Show resume" : "Hide resume";
      panel.hidden = open;
      if (open || panel.dataset.loaded) return;

      loading(panel, 4);
      try {
        const full = await api.getApplication(a.id);
        panel.dataset.loaded = "true";
        panel.replaceChildren(
          extracted(full),
          h("div", { class: "resume" }, full.resumeText),
          h("ul", { class: "history", "aria-label": "Status history" },
            full.statusHistory.map((s) => h("li", {}, `${s.status === "applied" ? "Applied" : capitalize(s.status)} ${formatDateTime(s.at)}`))));
      } catch (error) {
        errorState(panel, error);
      }
    },
  }, "Show resume");
  return h("div", { class: "details" }, toggle, panel);
}

load();
