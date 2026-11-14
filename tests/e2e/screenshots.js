// Captures the README screenshots from the seeded demo data (run `seed --reset` first).
//   cd tests/e2e && node screenshots.js
const { chromium } = require("playwright-core");
const path = require("path");

const BASE = process.env.BASE_URL ?? "http://localhost:5063";
const OUT = path.join(__dirname, "..", "..", "docs", "screenshots");
const EMAIL = process.env.E2E_RECRUITER_EMAIL ?? "recruiter@hirelens.dev";
const PASSWORD = process.env.E2E_RECRUITER_PASSWORD ?? "dev-recruiter-pass-2026";

(async () => {
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 860 }, deviceScaleFactor: 1.5 });
  const page = await ctx.newPage();

  await page.goto(`${BASE}/`); await page.waitForSelector(".job-row");
  await page.screenshot({ path: `${OUT}/candidate-roles.png` });

  await page.click(".job-row:has-text('Software Engineer (.NET)') >> text=View and apply");
  await page.waitForSelector("form");
  await page.screenshot({ path: `${OUT}/candidate-apply.png` });

  await page.goto(`${BASE}/login.html?returnUrl=/recruiter/`);
  await page.fill("#email", EMAIL); await page.fill("#password", PASSWORD);
  await Promise.all([page.waitForURL(`${BASE}/recruiter/`), page.click("button[type=submit]")]);
  await page.waitForSelector("table");

  const href = await page.getAttribute("tr:has-text('Software Engineer (.NET)') a:text('Applicants')", "href");
  await page.goto(BASE + href); await page.waitForSelector(".applicant");
  await page.click(".applicant:nth-child(4) .disclosure");
  await page.waitForSelector(".applicant:nth-child(4) .extracted");
  await page.screenshot({ path: `${OUT}/recruiter-ranked.png`, fullPage: true });

  await page.goto(`${BASE}/recruiter/job.html`); await page.waitForSelector("form");
  await page.fill("#title", "Backend Engineer");
  await page.fill("#location", "Bangalore");
  await page.fill("#descriptionText", "Build REST APIs in C# and ASP.NET Core on top of MongoDB and Redis. You'll containerise services with Docker, deploy to Azure and write unit tests with xUnit.");
  await page.click("#suggest");
  await page.waitForFunction(() => document.querySelector("#suggest-status")?.textContent.startsWith("Added"));
  await page.screenshot({ path: `${OUT}/recruiter-suggest-skills.png`, fullPage: true });

  const state = await ctx.storageState();
  const dark = await browser.newContext({ viewport: { width: 1280, height: 860 }, deviceScaleFactor: 1.5, colorScheme: "dark", storageState: state });
  const d = await dark.newPage();
  await d.goto(BASE + href); await d.waitForSelector(".applicant");
  await d.screenshot({ path: `${OUT}/recruiter-ranked-dark.png` });

  const mobile = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, storageState: state });
  const m = await mobile.newPage();
  await m.goto(BASE + href); await m.waitForSelector(".applicant");
  await m.screenshot({ path: `${OUT}/recruiter-ranked-mobile.png` });

  await browser.close();
  console.log("Saved screenshots to", OUT);
})().catch((e) => { console.error(e); process.exit(1); });
