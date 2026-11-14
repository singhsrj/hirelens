// Small DOM helpers shared by every page. User data is only ever inserted as text, never as HTML.

/** h("a", { href: "/x", class: "btn" }, "Text", childNode) */
export function h(tag, attrs = {}, ...children) {
  const el = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs ?? {})) {
    if (value === false || value === null || value === undefined) continue;
    if (key.startsWith("on") && typeof value === "function") el.addEventListener(key.slice(2), value);
    else if (key === "class") el.className = value;
    else if (key === "dataset") Object.assign(el.dataset, value);
    else el.setAttribute(key, value === true ? "" : String(value));
  }
  for (const child of children.flat()) {
    if (child === null || child === undefined || child === false) continue;
    el.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return el;
}

export const $ = (selector, root = document) => root.querySelector(selector);
export const param = (name) => new URLSearchParams(location.search).get(name);

const dateFormat = new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short", year: "numeric" });
const dateTimeFormat = new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short", hour: "numeric", minute: "2-digit" });
export const formatDate = (iso) => dateFormat.format(new Date(iso));
export const formatDateTime = (iso) => dateTimeFormat.format(new Date(iso));
export const percent = (score) => `${Math.round(score * 100)}%`;
export const capitalize = (s) => s.charAt(0).toUpperCase() + s.slice(1);

export function chips(skills, className = "") {
  return h("ul", { class: "chips" }, skills.map((s) => h("li", { class: `chip ${className}`.trim() }, s)));
}

/** Matched skills filled, missing skills dashed and struck through, in the job's must-have order. */
export function matchChips(mustHave, matched) {
  const hit = new Set(matched.map((s) => s.toLowerCase()));
  return h("ul", { class: "chips", "aria-label": "Must-have skills" },
    mustHave.map((s) => hit.has(s.toLowerCase())
      ? h("li", { class: "chip chip-match" }, h("span", { class: "visually-hidden" }, "Has "), s)
      : h("li", { class: "chip chip-miss" }, h("span", { class: "visually-hidden" }, "Missing "), h("s", {}, s))));
}

/** One segment per must-have skill. The visual reason behind the score. */
export function scoreStrip(mustHave, matched) {
  const hit = new Set(matched.map((s) => s.toLowerCase()));
  return h("div", { class: "strip", "aria-hidden": "true" },
    mustHave.map((s) => h("span", { class: hit.has(s.toLowerCase()) ? "hit" : "gap", title: s })));
}

export function loading(container, lines = 3) {
  container.replaceChildren(h("div", { class: "skeleton", "aria-busy": "true", "aria-label": "Loading" },
    Array.from({ length: lines }, () => h("span"))));
}

export function emptyState(container, title, text, action) {
  container.replaceChildren(h("div", { class: "state" }, h("h2", {}, title), h("p", {}, text), action ?? null));
}

export function errorState(container, error, onRetry) {
  container.replaceChildren(h("div", { class: "state state-error", role: "alert" },
    h("h2", {}, "This didn't load"),
    h("p", {}, error.message),
    onRetry ? h("button", { class: "btn", type: "button", onclick: onRetry }, "Try again") : null));
}

export function toast(message) {
  document.querySelector(".toast")?.remove();
  const el = h("div", { class: "toast", role: "status" }, message);
  document.body.append(el);
  setTimeout(() => el.remove(), 3200);
}

/** Show a toast on the next page load (e.g. after a redirect). */
export function flash(message) {
  try { sessionStorage.setItem("hirelens.flash", message); } catch { /* storage unavailable: skip */ }
}
export function showFlash() {
  try {
    const message = sessionStorage.getItem("hirelens.flash");
    if (message) { sessionStorage.removeItem("hirelens.flash"); toast(message); }
  } catch { /* storage unavailable: skip */ }
}

/**
 * Puts API validation errors next to the matching fields. The API returns PascalCase keys
 * ("MustHaveSkills"), the form uses camelCase names, so match case-insensitively.
 * Returns true if at least one field error was shown.
 */
export function showFieldErrors(form, fieldErrors) {
  clearFieldErrors(form);
  let shown = false;
  for (const [key, messages] of Object.entries(fieldErrors ?? {})) {
    const slot = [...form.querySelectorAll("[data-error-for]")]
      .find((el) => el.dataset.errorFor.toLowerCase() === key.toLowerCase());
    if (!slot) continue;
    slot.textContent = messages.join(" ");
    form.querySelector(`[aria-describedby~="${slot.id}"]`)?.setAttribute("aria-invalid", "true");
    shown = true;
  }
  return shown;
}

/** Clears a field's error as soon as the user edits it, so stale messages don't linger. */
export function clearErrorsOnInput(form) {
  form.addEventListener("input", (e) => {
    const control = e.target.closest("[aria-describedby]");
    if (!control) return;
    control.removeAttribute("aria-invalid");
    for (const id of control.getAttribute("aria-describedby").split(/\s+/)) {
      const slot = document.getElementById(id);
      if (slot?.dataset.errorFor) slot.textContent = "";
    }
  });
}

export function clearFieldErrors(form) {
  form.querySelectorAll("[data-error-for]").forEach((el) => (el.textContent = ""));
  form.querySelectorAll("[aria-invalid]").forEach((el) => el.removeAttribute("aria-invalid"));
}

/** Disables a submit button and marks it busy while an async action runs. */
export async function busy(button, label, action) {
  const original = button.textContent;
  button.disabled = true;
  button.setAttribute("aria-busy", "true");
  button.textContent = label;
  try { return await action(); }
  finally {
    button.disabled = false;
    button.removeAttribute("aria-busy");
    button.textContent = original;
  }
}

/**
 * Turns a container into a skill input: type a skill and press Enter or comma to add it,
 * Backspace on an empty box removes the last one. Pasting "a, b, c" adds all three.
 */
export function skillInput(container, { id, describedBy, placeholder = "Type a skill, press Enter" } = {}) {
  let skills = [];
  const input = h("input", { type: "text", id, placeholder, autocomplete: "off", "aria-describedby": describedBy });
  const list = h("span", { class: "chips", role: "list" });
  container.classList.add("skill-input");
  container.replaceChildren(list, input);
  container.addEventListener("click", (e) => { if (e.target === container) input.focus(); });

  const render = () => {
    list.replaceChildren(...skills.map((skill, i) =>
      h("span", { class: "chip", role: "listitem" }, skill,
        h("button", { type: "button", "aria-label": `Remove ${skill}`, onclick: () => { skills.splice(i, 1); render(); input.focus(); } }, "×"))));
    container.dispatchEvent(new Event("change", { bubbles: true }));
  };

  const add = (text) => {
    let changed = false;
    for (const raw of text.split(",")) {
      const skill = raw.trim().replace(/\s+/g, " ");
      if (skill && !skills.some((s) => s.toLowerCase() === skill.toLowerCase())) { skills.push(skill); changed = true; }
    }
    if (changed) render();
  };

  input.addEventListener("keydown", (e) => {
    if (e.key === "Enter" || e.key === ",") {
      e.preventDefault();
      add(input.value);
      input.value = "";
    } else if (e.key === "Backspace" && input.value === "" && skills.length) {
      skills.pop();
      render();
    }
  });
  input.addEventListener("paste", (e) => {
    const text = e.clipboardData?.getData("text") ?? "";
    if (text.includes(",")) { e.preventDefault(); add(text); }
  });
  // Don't lose a half-typed skill when the user tabs away or submits.
  input.addEventListener("blur", () => { if (input.value.trim()) { add(input.value); input.value = ""; } });

  return {
    get: () => { if (input.value.trim()) { add(input.value); input.value = ""; } return [...skills]; },
    set: (list) => { skills = []; add((list ?? []).join(",")); if (!list?.length) render(); },
    input,
  };
}
