// Job detail + application form for candidates.
import { api } from "../api.js";
import {
  $, h, param, chips, formatDate, loading, errorState, emptyState,
  skillInput, showFieldErrors, clearFieldErrors, clearErrorsOnInput, busy,
} from "../ui.js";

const host = $("#job");
const jobId = param("id");

async function load() {
  if (!jobId) {
    emptyState(host, "No role selected", "Pick a role from the list to see its details.",
      h("a", { class: "btn", href: "/" }, "See open roles"));
    return;
  }
  loading(host, 5);
  try {
    const job = await api.getJob(jobId);
    document.title = `${job.title} | HireLens`;
    render(job);
  } catch (error) {
    if (error.status === 404) {
      emptyState(host, "This role doesn't exist", "It may have been removed. Other roles are still open.",
        h("a", { class: "btn", href: "/" }, "See open roles"));
    } else {
      errorState(host, error, load);
    }
  }
}

function render(job) {
  const body = h("article", { class: "job-body" },
    h("h1", {}, job.title),
    h("p", { class: "meta" }, [job.location || "Location not specified", `Posted ${formatDate(job.createdAt)}`].join(", ")),
    h("section", { class: "job-section" }, h("h2", {}, "Must-have skills"), chips(job.mustHaveSkills)),
    job.niceToHaveSkills.length
      ? h("section", { class: "job-section" }, h("h2", {}, "Nice to have"), chips(job.niceToHaveSkills))
      : null,
    h("section", { class: "job-section" }, h("h2", {}, "About the role"), h("p", { class: "description" }, job.descriptionText)));

  const panel = h("aside", { class: "panel apply-panel", "aria-labelledby": "apply-heading" });
  if (job.status === "open") renderForm(panel, job);
  else panel.append(h("h2", { id: "apply-heading" }, "Applications closed"),
    h("p", { class: "muted" }, "This role is no longer taking applications."));

  host.replaceChildren(h("div", { class: "job-layout" }, body, panel));
}

function renderForm(panel, job) {
  const form = h("form", { class: "form", novalidate: true },
    field("name", "Full name", h("input", { type: "text", id: "name", name: "name", autocomplete: "name", required: true, maxlength: 100, "aria-describedby": "name-error" })),
    field("email", "Email", h("input", { type: "email", id: "email", name: "email", autocomplete: "email", required: true, maxlength: 254, "aria-describedby": "email-error" })),
    field("resumeText", "Resume",
      h("textarea", { id: "resumeText", name: "resumeText", required: true, rows: 9, "aria-describedby": "resumeText-hint resumeText-error" }),
      "Paste the text of your resume. At least 50 characters."),
    field("skills", "Your skills", h("div", { id: "skills-box" }),
      "Add each skill you want considered for this role."),
    h("p", { class: "form-error", role: "alert", id: "form-error" }),
    h("div", { class: "form-actions" }, h("button", { class: "btn btn-primary", type: "submit" }, "Send application")));

  // Phase 2-3: candidates list skills by hand. Phase 4 extracts them from the resume with NER.
  const skills = skillInput(form.querySelector("#skills-box"), { id: "skills", describedBy: "skills-hint skills-error" });

  clearErrorsOnInput(form);
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    clearFieldErrors(form);
    $("#form-error", form).textContent = "";

    const data = {
      name: form.elements.namedItem("name").value.trim(),
      email: form.elements.namedItem("email").value.trim(),
      resumeText: form.elements.namedItem("resumeText").value.trim(),
      skills: skills.get(),
    };

    const local = validate(data);
    if (Object.keys(local).length) {
      showFieldErrors(form, local);
      form.querySelector("[aria-invalid='true']")?.focus();
      return;
    }

    try {
      await busy(form.querySelector("[type=submit]"), "Sending…", () => api.apply(job.id, data));
      panel.replaceChildren(h("div", { class: "success", role: "status", tabindex: "-1" },
        h("h2", { id: "apply-heading" }, "Application sent"),
        h("p", {}, `Thanks, ${data.name}. The recruiter will contact you at ${data.email}.`)));
      panel.querySelector(".success").focus();
    } catch (error) {
      const shown = showFieldErrors(form, error.fieldErrors);
      $("#form-error", form).textContent = error.message;
      if (shown) form.querySelector("[aria-invalid='true']")?.focus();
    }
  });

  panel.replaceChildren(
    h("h2", { id: "apply-heading" }, "Apply for this role"),
    h("p", { class: "muted small" }, "We match your skills against the must-haves listed for this role."),
    form);
}

// Mirrors the API's rules so most mistakes are caught before a round trip. The API stays the authority.
function validate(d) {
  const errors = {};
  if (d.name.length < 2) errors.name = ["Enter your full name."];
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(d.email)) errors.email = ["Enter an email address like name@example.com."];
  if (d.resumeText.length < 50) errors.resumeText = [`Your resume needs at least 50 characters (currently ${d.resumeText.length}).`];
  return errors;
}

function field(name, label, control, hint) {
  return h("div", { class: "field" },
    h("label", { for: name }, label),
    control,
    hint ? h("p", { class: "hint", id: `${name}-hint` }, hint) : null,
    h("p", { class: "error", id: `${name}-error`, dataset: { errorFor: name } }));
}

load();
