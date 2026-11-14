// Benchmarks the recruiter's ranked-applicants query with and without the compound index.
// Uses a throwaway database (hirelens_bench) that is dropped at the end; the app's data is untouched.
//
//   docker exec -i hirelens-mongo mongosh --quiet < tools/benchmark/ranked-query.js
//
// The query is exactly what ApplicationRepository.ListRankedAsync sends:
//   find({ jobId }) .sort({ score: -1, createdAt: 1 }) .projection({ resumeText: 0 })

const JOBS = 100;
const APPS_PER_JOB = 500;          // 50,000 applications in total
const RESUME_CHARS = 1500;         // realistic document size, so a collection scan costs what it would in production
const RUNS = 25;

const bench = db.getSiblingDB("hirelens_bench");
bench.dropDatabase();
const apps = bench.applications;

print(`Inserting ${JOBS * APPS_PER_JOB} applications across ${JOBS} jobs...`);
const filler = "x".repeat(RESUME_CHARS);
const jobIds = Array.from({ length: JOBS }, () => new ObjectId());
const t0 = Date.now();
for (const jobId of jobIds) {
  const batch = [];
  for (let i = 0; i < APPS_PER_JOB; i++) {
    batch.push({
      jobId,
      name: `Candidate ${i}`,
      email: `c${i}@example.com`,
      resumeText: filler,
      nerStatus: "done",
      skills: ["C#", "MongoDB"],
      score: Math.round(Math.random() * 5) / 5,       // 0, 0.2 ... 1 like a 5-skill job
      matched: [], missing: [],
      status: "applied",
      createdAt: new Date(Date.now() - Math.floor(Math.random() * 30 * 864e5)),
    });
  }
  apps.insertMany(batch, { ordered: false });
}
print(`  done in ${((Date.now() - t0) / 1000).toFixed(1)} s`);

const target = jobIds[Math.floor(JOBS / 2)];
const query = () => apps.find({ jobId: target }, { resumeText: 0 }).sort({ score: -1, createdAt: 1 });

function measure(label, hint) {
  const run = () => (hint ? query().hint(hint) : query()).toArray();
  run(); run();                                    // warm the cache so we compare query plans, not disk reads
  const times = [];
  for (let i = 0; i < RUNS; i++) {
    const start = Date.now();
    const docs = run();
    times.push(Date.now() - start);
    if (docs.length !== APPS_PER_JOB) throw new Error(`expected ${APPS_PER_JOB} docs, got ${docs.length}`);
  }
  times.sort((a, b) => a - b);
  const plan = (hint ? query().hint(hint) : query()).explain("executionStats");
  const stats = plan.executionStats;
  const stages = JSON.stringify(plan.queryPlanner.winningPlan).match(/"stage":"[A-Z_]+"/g).map((s) => s.slice(9, -1));
  const result = {
    label,
    medianMs: times[Math.floor(RUNS / 2)],
    p95Ms: times[Math.floor(RUNS * 0.95)],
    docsExamined: stats.totalDocsExamined,
    keysExamined: stats.totalKeysExamined,
    inMemorySort: stages.includes("SORT"),
    plan: [...new Set(stages)].join(" > "),
  };
  printjson(result);
  return result;
}

print(`\nRanked list for one job (${APPS_PER_JOB} of ${JOBS * APPS_PER_JOB} applications), ${RUNS} runs each:\n`);
const without = measure("no index (collection scan)", { $natural: 1 });

apps.createIndex({ jobId: 1, score: -1, createdAt: 1 }, { name: "jobId_score_createdAt" });
const withIdx = measure("index { jobId: 1, score: -1, createdAt: 1 }", "jobId_score_createdAt");

print(`\nSummary: median ${without.medianMs} ms -> ${withIdx.medianMs} ms ` +
      `(${(without.medianMs / Math.max(withIdx.medianMs, 1)).toFixed(1)}x faster); ` +
      `documents examined ${without.docsExamined} -> ${withIdx.docsExamined}; ` +
      `in-memory sort ${without.inMemorySort ? "yes" : "no"} -> ${withIdx.inMemorySort ? "yes" : "no"}.`);

bench.dropDatabase();
