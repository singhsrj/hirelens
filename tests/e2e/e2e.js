// End-to-end browser test for the HireLens UI: drives the recruiter and candidate flows in
// Microsoft Edge (via playwright-core, no browser download) and saves screenshots to ./shots.
//
// Needs MongoDB + the NER service running (docker compose up -d), and the API with a relaxed
// apply rate limit (the test submits several applications):
//   RateLimiting__Apply__PermitLimit=100 dotnet run --project src/HireLens.Api
// Then:  cd tests/e2e && npm install && npm test
// Each run creates its own uniquely titled job, so it can run against a database that already has data.
const { chromium } = require("playwright-core");
const path = require("path");
const fs = require("fs");

const BASE = process.env.BASE_URL ?? "http://localhost:5063";
const TITLE = `Software Engineer e2e-${Date.now().toString(36)}`;
const OUT = path.join(__dirname, "shots");
fs.mkdirSync(OUT, { recursive: true });
const RESUME = (extra) =>
  `Software engineer with five years of experience building web APIs and services. ${extra} Worked across agile teams, wrote tests and shipped to production weekly.`;

const results = [];
const check = (name, ok, detail = "") => { results.push({ name, ok }); console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail ? "  -- " + detail : ""}`); };

(async () => {
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await ctx.newPage();
  const consoleErrors = [];
  page.on("console", (m) => m.type() === "error" && !m.text().startsWith("Failed to load resource") && consoleErrors.push(m.text()));
  page.on("pageerror", (e) => consoleErrors.push(String(e)));

  // ---------- Recruiter: empty state + create job ----------
  await page.goto(`${BASE}/recruiter/`);
  await page.waitForSelector(".state, table");
  check("recruiter jobs page loads", true);

  await page.click("text=Create job >> nth=0");
  await page.waitForSelector("form");
  await page.click("button[type=submit]");
  const errs = await page.$$eval(".field .error", (els) => els.map((e) => e.textContent).filter(Boolean));
  check("create form shows client validation", errs.length === 3, errs.join(" | "));
  await page.screenshot({ path: `${OUT}/01-create-validation.png`, fullPage: true });

  await page.fill("#title", TITLE);
  await page.fill("#location", "Remote");
  await page.fill("#descriptionText", "Build ASP.NET Core APIs in C# backed by MongoDB, plus plain JavaScript frontends.\n\nYou'll own features end to end.");
  await page.click("#suggest");
  await page.waitForFunction(() => document.querySelector("#suggest-status")?.textContent.startsWith("Added"));
  const suggested = await page.$$eval("#must-box .chip", (els) => els.map((e) => e.firstChild.textContent));
  check("suggest skills fills must-haves from the description", suggested.join() === "ASP.NET Core,C#,MongoDB,JavaScript", suggested.join());
  await page.click("#must-box button[aria-label='Remove ASP.NET Core']");
  await page.fill("#mustHaveSkills", "HTML"); await page.press("#mustHaveSkills", "Enter");
  await page.fill("#niceToHaveSkills", "Python, Docker"); await page.press("#niceToHaveSkills", "Enter");
  check("skill chips added", (await page.$$("#must-box .chip")).length === 4 && (await page.$$("#nice-box .chip")).length === 2);
  await page.press("#mustHaveSkills", "Backspace"); // removes HTML
  check("backspace removes last chip", (await page.$$("#must-box .chip")).length === 3);
  await page.screenshot({ path: `${OUT}/02-create-filled.png`, fullPage: true });
  await page.click("button[type=submit]");
  await page.waitForURL(`${BASE}/recruiter/`);
  await page.waitForSelector("table");
  check("job created, toast shown", (await page.textContent(".toast")).includes(`Created ${TITLE}`));
  await page.screenshot({ path: `${OUT}/03-recruiter-jobs.png`, fullPage: true });

  const jobId = new URL(await page.getAttribute(`tr:has-text("${TITLE}") a:text('Applicants')`, "href"), BASE).searchParams.get("jobId");

  // ---------- Candidate: list, detail, apply ----------
  await page.goto(BASE + "/");
  await page.waitForSelector(".job-row");
  check("public list shows job", (await page.$(`.job-row:has-text("${TITLE}")`)) !== null);
  await page.screenshot({ path: `${OUT}/04-public-list.png`, fullPage: true });

  await page.click(`.job-row:has-text("${TITLE}") >> text=View and apply`);
  await page.waitForSelector("form");
  await page.fill("#name", "Asha Rao");
  await page.fill("#email", "bad-email");
  await page.fill("#resumeText", "too short");
  await page.click("button[type=submit]");
  check("apply form client validation", (await page.textContent("#email-error")).length > 0 && (await page.textContent("#resumeText-error")).includes("50"));
  await page.fill("#email", "asha@example.com");
  check("field error clears on input", (await page.textContent("#email-error")) === "" && !(await page.getAttribute("#email", "aria-invalid")));
  await page.fill("#resumeText", RESUME("Mostly C#, ASP.NET Core and MongoDB, some JavaScript."));
  check("apply form has no manual skills field", (await page.$("#skills")) === null);
  await page.screenshot({ path: `${OUT}/05-apply-filled.png`, fullPage: true });
  await page.click("button[type=submit]");
  await page.waitForSelector(".success");
  check("application sent", (await page.textContent(".success h2")) === "Application sent");
  await page.screenshot({ path: `${OUT}/06-apply-success.png`, fullPage: true });

  // Duplicate shows server message
  await page.reload(); await page.waitForSelector("form");
  await page.fill("#name", "Asha Again"); await page.fill("#email", "ASHA@example.com");
  await page.fill("#resumeText", RESUME("Same person again."));
  await page.click("button[type=submit]");
  await page.waitForFunction(() => document.querySelector("#form-error")?.textContent);
  check("duplicate application message", (await page.textContent("#form-error")).includes("already exists"));

  // More applicants through the API
  const more = [["Ben Ortiz", ["C#"]], ["Chen Li", ["C#", "JavaScript"]], ["Dana Kim", []], ["Eli Novak", ["MongoDB", "JavaScript", "C#"]]];
  for (const [name, skills] of more) {
    const r = await page.request.post(`${BASE}/api/jobs/${jobId}/applications`, {
      data: { name, email: name.split(" ")[0].toLowerCase() + "@example.com", resumeText: RESUME(`Skills: ${skills.join(", ") || "mostly Java"}.`) },
    });
    if (r.status() !== 201) throw new Error(`seed failed ${r.status()}`);
  }

  // ---------- Recruiter: ranked applicants ----------
  await page.goto(`${BASE}/recruiter/applicants.html?jobId=${jobId}`);
  await page.waitForSelector(".applicant");
  const names = await page.$$eval(".applicant .who strong", (els) => els.map((e) => e.textContent));
  const scores = await page.$$eval(".applicant .score-value", (els) => els.map((e) => e.textContent));
  check("ranked order", names.join(",") === "Asha Rao,Eli Novak,Chen Li,Ben Ortiz,Dana Kim", names.join(",") + " / " + scores.join(","));
  check("strip segments = must-haves", (await page.$$(".applicant:first-child .strip span")).length === 3);
  await page.screenshot({ path: `${OUT}/07-applicants.png`, fullPage: true });

  await page.click(".applicant:nth-child(3) button:text('Shortlist')");
  await page.waitForSelector(".applicant:nth-child(3) .status-shortlisted");
  check("shortlist updates row", true);
  await page.click(".applicant:nth-child(5) button:text('Reject')");
  await page.waitForSelector(".applicant:nth-child(5) .status-rejected");
  await page.click(".segmented button:has-text('Shortlisted')");
  const shortlisted = await page.$$eval(".applicant .who strong", (els) => els.map((e) => e.textContent));
  const rankKept = await page.textContent(".applicant .rank");
  check("filter shortlisted keeps rank", shortlisted.join() === "Chen Li" && rankKept === "3", `${shortlisted} rank ${rankKept}`);
  await page.click(".segmented button:has-text('All')");
  await page.click(".applicant:nth-child(1) .disclosure");
  await page.waitForSelector(".applicant:nth-child(1) .resume");
  check("resume loads on demand", (await page.textContent(".applicant:nth-child(1) .resume")).includes("ASP.NET Core"));
  const extracted = await page.textContent(".applicant:nth-child(1) .extracted");
  check("extracted details list every skill found", extracted.includes("All skills found") && extracted.includes("ASP.NET Core") && !extracted.includes("[object"), extracted);
  await page.click(".applicant:nth-child(3) .disclosure");
  await page.waitForSelector(".applicant:nth-child(3) .history li:nth-child(2)");
  check("history shows shortlisted", (await page.textContent(".applicant:nth-child(3) .history")).includes("Shortlisted"));
  await page.screenshot({ path: `${OUT}/08-applicants-actions.png`, fullPage: true });

  // ---------- Recruiter: edit job (drop JavaScript -> rescore), then close ----------
  await page.goto(`${BASE}/recruiter/job.html?id=${jobId}`);
  await page.waitForSelector("#must-box .chip");
  check("edit form prefilled", (await page.inputValue("#title")) === TITLE && (await page.$$("#must-box .chip")).length === 3);
  await page.click("#must-box button[aria-label='Remove JavaScript']");
  await page.selectOption("#status", "closed");
  await page.click("button[type=submit]");
  await page.waitForURL(`${BASE}/recruiter/`);
  await page.waitForSelector(".state, table");
  check("closed job leaves open list", !(await page.textContent("main")).includes(TITLE));

  await page.goto(`${BASE}/recruiter/applicants.html?jobId=${jobId}`);
  await page.waitForSelector(".applicant");
  const chenScore = await page.textContent(".applicant:has-text('Chen Li') .score-value");
  check("rescored after edit (Chen 1 of 2 = 50%)", chenScore === "50%", chenScore);

  await page.goto(`${BASE}/job.html?id=${jobId}`);
  await page.waitForSelector(".apply-panel h2");
  check("closed job shows no form", (await page.textContent(".apply-panel h2")) === "Applications closed");

  // ---------- Error + 404 states ----------
  await page.goto(`${BASE}/job.html?id=aaaaaaaaaaaaaaaaaaaaaaaa`);
  await page.waitForSelector(".state h2");
  check("404 job state", (await page.textContent(".state h2")).includes("doesn't exist"));

  // ---------- Mobile + dark ----------
  await page.request.put(`${BASE}/api/jobs/${jobId}`, { data: { title: TITLE, location: "Remote", descriptionText: "Build ASP.NET Core APIs in C# backed by MongoDB, plus plain JavaScript frontends.", mustHaveSkills: ["C#", "MongoDB", "JavaScript"], niceToHaveSkills: ["Python"], status: "open" } });
  const mobile = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2 });
  const m = await mobile.newPage();
  m.on("pageerror", (e) => consoleErrors.push(String(e)));
  await m.goto(`${BASE}/recruiter/applicants.html?jobId=${jobId}`); await m.waitForSelector(".applicant");
  const overflow = await m.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  check("mobile: no horizontal scroll (applicants)", !overflow);
  await m.screenshot({ path: `${OUT}/09-mobile-applicants.png`, fullPage: true });
  await m.goto(`${BASE}/job.html?id=${jobId}`); await m.waitForSelector("form");
  check("mobile: no horizontal scroll (job)", !(await m.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)));
  await m.screenshot({ path: `${OUT}/10-mobile-job.png`, fullPage: true });
  await m.goto(`${BASE}/recruiter/`); await m.waitForSelector("table");
  check("mobile: no horizontal scroll (jobs)", !(await m.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)));
  await m.screenshot({ path: `${OUT}/11-mobile-jobs.png`, fullPage: true });

  const dark = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: "dark" });
  const d = await dark.newPage();
  await d.goto(`${BASE}/recruiter/applicants.html?jobId=${jobId}`); await d.waitForSelector(".applicant");
  await d.screenshot({ path: `${OUT}/12-dark-applicants.png`, fullPage: true });

  check("no console errors", consoleErrors.length === 0, consoleErrors.join(" | "));
  await browser.close();
  const failed = results.filter((r) => !r.ok).length;
  console.log(`\n${results.length - failed}/${results.length} checks passed`);
  process.exit(failed ? 1 : 0);
})().catch((e) => { console.error(e); process.exit(1); });
