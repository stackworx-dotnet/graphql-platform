// Join corpus + graphql-js validity + StrawberryShake codegen results into findings.
//
//   node report.mjs corpus.jsonl node-results.jsonl hc-results.jsonl out.json
//
// A FINDING is a document graphql-js declares VALID but StrawberryShake either
// (a) throws while analyzing, (b) throws while generating, (c) returns generation
// errors, or (d) emits C# that does not compile. Findings are grouped by a stable
// signature so thousands of corpus lines collapse into a handful of distinct bugs.
import { readFileSync, writeFileSync } from 'node:fs';

const [corpusPath, nodePath, hcPath, outPath = 'out.json'] = process.argv.slice(2);
if (!corpusPath || !nodePath || !hcPath) {
  console.error('usage: node report.mjs <corpus.jsonl> <node-results.jsonl> <hc-results.jsonl> [out.json]');
  process.exit(2);
}

const readJsonl = (p) => readFileSync(p, 'utf8').split('\n').filter(l => l.trim()).map(l => JSON.parse(l));
const corpus = readFileSync(corpusPath, 'utf8').split('\n').filter(l => l.trim()).map(l => JSON.parse(l));
const nodeRes = readJsonl(nodePath);
const hcRes = readJsonl(hcPath);

const nodeByI = new Map(nodeRes.map(r => [r.i, r]));
const hcByI = new Map(hcRes.map(r => [r.i, r]));

const groups = new Map(); // signature -> { category, signature, count, examples:[{query,detail}] }
function add(category, signature, query, detail) {
  const key = `${category}::${signature}`;
  let g = groups.get(key);
  if (!g) { g = { category, signature, count: 0, examples: [] }; groups.set(key, g); }
  g.count++;
  if (g.examples.length < 5) g.examples.push({ query, detail });
}

let total = 0, validCount = 0;
const counts = { analyze_threw: 0, gen_threw: 0, gen_error: 0, compile_error: 0, parse_disagree: 0 };

for (let i = 0; i < corpus.length; i++) {
  const q = corpus[i];
  const nr = nodeByI.get(i);
  const hr = hcByI.get(i);
  if (!nr || !hr) continue;
  total++;
  if (!nr.valid) continue; // only care about graphql-js-valid docs
  validCount++;

  if (hr.parseError) {
    counts.parse_disagree++;
    add('parse_disagree', firstLine(hr.parseError), q, hr.parseError);
    continue;
  }
  if (hr.analyzeThrew) {
    counts.analyze_threw++;
    add('analyze_threw', normalizeMsg(hr.analyzeThrew), q, hr.analyzeThrew);
    continue;
  }
  if (hr.genThrew) {
    counts.gen_threw++;
    add('gen_threw', normalizeMsg(hr.genThrew), q, hr.genThrew);
    continue;
  }
  if (hr.genErrors && hr.genErrors.length > 0) {
    counts.gen_error++;
    const sig = hr.genErrors.map(e => e.code || normalizeMsg(e.message)).sort().join('|');
    add('gen_error', sig, q, JSON.stringify(hr.genErrors));
    continue;
  }
  if (hr.compiled === false) {
    counts.compile_error++;
    const ids = (hr.compileErrors || []).map(e => e.id).sort();
    const sig = [...new Set(ids)].join('|') || 'unknown';
    add('compile_error', sig, q, JSON.stringify((hr.compileErrors || []).slice(0, 4)));
    continue;
  }
}

function firstLine(s) { return String(s).split('\n')[0].slice(0, 120); }
function normalizeMsg(s) {
  return String(s)
    .replace(/`[^`]*`/g, '`X`')          // collapse quoted identifiers
    .replace(/'[^']*'/g, "'X'")
    .replace(/\b\d+\b/g, 'N')            // collapse numbers
    .split('\n')[0]
    .slice(0, 140);
}

const findings = [...groups.values()].sort((a, b) => b.count - a.count);
const summary = {
  totalCompared: total,
  graphqlJsValid: validCount,
  findingDocs: Object.values(counts).reduce((a, b) => a + b, 0),
  byCategory: counts,
  distinctSignatures: findings.length,
};

writeFileSync(outPath, JSON.stringify({ summary, findings }, null, 2));

console.log('=== StrawberryShake codegen differential ===');
console.log(JSON.stringify(summary, null, 2));
console.log('\n=== distinct findings (by signature) ===');
for (const f of findings) {
  console.log(`\n[${f.category}] (${f.count}x) sig=${f.signature}`);
  const ex = f.examples[0];
  console.log(`  e.g. ${ex.query}`);
  console.log(`  ->  ${firstLine(ex.detail)}`);
}
console.log(`\nfull report: ${outPath}`);
