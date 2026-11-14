// Recruiter sign-in. On success, returns to the page that sent the recruiter here.
import { api } from "../api.js";
import { $, showFieldErrors, clearFieldErrors, clearErrorsOnInput, busy } from "../ui.js";

const form = $("#login");

/** Only same-site paths, so a crafted link can't bounce the recruiter to another site after sign-in. */
function safeReturnUrl() {
  const target = new URLSearchParams(location.search).get("returnUrl") ?? "";
  const sameSite = target.startsWith("/") && !target.startsWith("//") && !target.startsWith("/\\");
  return sameSite ? target : "/recruiter/";
}

clearErrorsOnInput(form);
form.addEventListener("submit", async (e) => {
  e.preventDefault();
  clearFieldErrors(form);
  $("#form-error").textContent = "";

  const email = form.elements.namedItem("email").value.trim();
  const password = form.elements.namedItem("password").value;
  const errors = {};
  if (!email) errors.email = ["Enter your email."];
  if (!password) errors.password = ["Enter your password."];
  if (Object.keys(errors).length) {
    showFieldErrors(form, errors);
    form.querySelector("[aria-invalid='true']")?.focus();
    return;
  }

  try {
    await busy(form.querySelector("[type=submit]"), "Signing in…", () => api.login(email, password));
    location.href = safeReturnUrl();
  } catch (error) {
    $("#form-error").textContent = error.message;
    form.elements.namedItem("password").select();
  }
});

form.elements.namedItem("email").focus();
