// Recruiter: create a job, or edit one when ?id= is present.
import { api } from "../api.js";
import {
  $, h, param, loading, errorState, emptyState,
  skillInput, showFieldErrors, clearFieldErrors, clearErrorsOnInput, busy, flash,
} from "../ui.js";

const host = $("#form-host");
const jobId = param("id");

async function load() {
  if (!jobId) return render(null);

  loading(host, 6);
  try {
    render(await api.getJob(jobId));
  } catch (error) {
    if (error.status === 404) {
      emptyState(host, "This job doesn't exist", "It may have been deleted.",
        h("a", { class: "btn", href: "/recruiter/" }, "Back to jobs"));
    } else {
      errorState(host, error, load);
    }
  }
}

function render(job) {
  const editing = job !== null;
  $("#heading").textContent = editing ? `Edit ${job.title}` : "Create job";
  document.title = `${editing ? "Edit job" : "Create job"} | HireLens`;

  const form = h("form", { class: "form panel", novalidate: true, style: "max-width: 760px" },
    h("div", { class: "form-row" },
      field("title", "Job title", h("input", { type: "text", id: "title", name: "title", required: true, maxlength: 120, "aria-describedby": "title-error" })),
      field("location", "Location", h("input", { type: "text", id: "location", name: "location", maxlength: 120, placeholder: "e.g. Remote, or Pune", "aria-describedby": "location-error" }))),
    field("descriptionText", "Job description",
      h("textarea", { id: "descriptionText", name: "descriptionText", required: true, rows: 12, "aria-describedby": "descriptionText-hint descriptionText-error" }),
      "Paste the full job description. Candidates see this on the role page."),
    field("mustHaveSkills", "Must-have skills", h("div", { id: "must-box" }),
      "Applicants are ranked by how many of these they have."),
    field("niceToHaveSkills", "Nice-to-have skills", h("div", { id: "nice-box" }),
      "Shown to candidates. These don't change the score."),
    editing
      ? field("status", "Status", h("select", { id: "status", name: "status", "aria-describedby": "status-hint status-error" },
          h("option", { value: "open" }, "Open, accepting applications"),
          h("option", { value: "closed" }, "Closed, no new applications")),
          "Closing a job hides it from the public list. Existing applicants stay.")
      : null,
    h("p", { class: "form-error", role: "alert", id: "form-error" }),
    h("div", { class: "form-actions" },
      h("button", { class: "btn btn-primary", type: "submit" }, editing ? "Save changes" : "Create job"),
      h("a", { class: "btn", href: "/recruiter/" }, "Cancel")));

  const mustHave = skillInput(form.querySelector("#must-box"), { id: "mustHaveSkills", describedBy: "mustHaveSkills-hint mustHaveSkills-error" });
  const niceToHave = skillInput(form.querySelector("#nice-box"), { id: "niceToHaveSkills", describedBy: "niceToHaveSkills-hint niceToHaveSkills-error" });

  if (editing) {
    form.elements.namedItem("title").value = job.title;
    form.elements.namedItem("location").value = job.location;
    form.elements.namedItem("descriptionText").value = job.descriptionText;
    form.elements.namedItem("status").value = job.status;
    mustHave.set(job.mustHaveSkills);
    niceToHave.set(job.niceToHaveSkills);
  }

  clearErrorsOnInput(form);
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    clearFieldErrors(form);
    $("#form-error", form).textContent = "";

    const data = {
      title: form.elements.namedItem("title").value.trim(),
      location: form.elements.namedItem("location").value.trim() || null,
      descriptionText: form.elements.namedItem("descriptionText").value.trim(),
      mustHaveSkills: mustHave.get(),
      niceToHaveSkills: niceToHave.get(),
      status: editing ? form.elements.namedItem("status").value : "open",
    };

    const local = validate(data);
    if (Object.keys(local).length) {
      showFieldErrors(form, local);
      form.querySelector("[aria-invalid='true']")?.focus();
      return;
    }

    try {
      const saved = await busy(form.querySelector("[type=submit]"), "Saving…",
        () => editing ? api.updateJob(job.id, data) : api.createJob(data));
      flash(editing ? `Saved changes to ${saved.title}` : `Created ${saved.title}`);
      location.href = "/recruiter/";
    } catch (error) {
      const shown = showFieldErrors(form, error.fieldErrors);
      $("#form-error", form).textContent = error.message;
      if (shown) form.querySelector("[aria-invalid='true']")?.focus();
    }
  });

  host.replaceChildren(form);
}

function validate(d) {
  const errors = {};
  if (d.title.length < 2) errors.title = ["Enter a job title."];
  if (d.descriptionText.length < 20) errors.descriptionText = ["The description needs at least 20 characters."];
  if (d.mustHaveSkills.length === 0) errors.mustHaveSkills = ["Add at least one must-have skill."];
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
