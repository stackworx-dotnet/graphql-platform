// Compare reference (graphql-js) vs HotChocolate validation results.
// Usage: node diff.mjs <corpus.jsonl> <node-results.jsonl> <hc-results.jsonl> [out.json]
//
// Categories (both parse OK unless noted):
//   hc_missed     : reference INVALID, HotChocolate VALID  -> HC missed a spec violation (PRIME signal)
//   hc_extra      : reference VALID,   HotChocolate INVALID -> HC stricter than the spec
//   parse_disagree: one parser errored and the other did not
import { readFileSync, writeFileSync } from 'node:fs';

const [corpusPath, nodePath, hcPath, outPath] = process.argv.slice(2);
if (!corpusPath || !nodePath || !hcPath) {
  console.error('usage: node diff.mjs <corpus.jsonl> <node-results.jsonl> <hc-results.jsonl> [out.json]');
  process.exit(2);
}

const readJsonl = (p) =>
  readFileSync(p, 'utf8')
    .split('\n')
    .filter((l) => l.trim().length > 0)
    .map((l) => JSON.parse(l));

const corpus = readFileSync(corpusPath, 'utf8')
  .split('\n')
  .filter((l) => l.trim().length > 0)
  .map((l) => JSON.parse(l));

const nodeBy = new Map(readJsonl(nodePath).map((r) => [r.i, r]));
const hcBy = new Map(readJsonl(hcPath).map((r) => [r.i, r]));

const buckets = { hc_missed: [], hc_extra: [], parse_disagree: [] };
let compared = 0;
let agree = 0;

for (const [i, n] of nodeBy) {
  const h = hcBy.get(i);
  if (!h) continue;
  compared++;
  const query = corpus[i];

  if (n.parseError !== h.parseError) {
    buckets.parse_disagree.push({ i, query, node: summarize(n), hc: summarize(h) });
    continue;
  }
  if (n.parseError && h.parseError) {
    agree++;
    continue; // both rejected at parse time; not a validation signal
  }
  if (!n.valid && h.valid) {
    buckets.hc_missed.push({ i, query, refErrors: n.messages });
  } else if (n.valid && !h.valid) {
    buckets.hc_extra.push({ i, query, hcErrors: h.messages });
  } else {
    agree++;
  }
}

function summarize(r) {
  return { parseError: r.parseError, valid: r.valid, errorCount: r.errorCount, messages: r.messages };
}

const report = {
  summary: {
    compared,
    agree,
    hc_missed: buckets.hc_missed.length,
    hc_extra: buckets.hc_extra.length,
    parse_disagree: buckets.parse_disagree.length,
  },
  hc_missed: buckets.hc_missed,
  hc_extra: buckets.hc_extra,
  parse_disagree: buckets.parse_disagree,
};

if (outPath) {
  writeFileSync(outPath, JSON.stringify(report, null, 2));
}

// Console summary + the prime signal.
console.log('=== differential validation summary ===');
console.log(JSON.stringify(report.summary, null, 2));
const show = (title, arr, fmt) => {
  if (!arr.length) return;
  console.log(`\n=== ${title} (${arr.length}) ===`);
  for (const x of arr.slice(0, 30)) console.log(fmt(x));
  if (arr.length > 30) console.log(`... and ${arr.length - 30} more (see ${outPath ?? 'out.json'})`);
};
show('HC MISSED (reference invalid, HC valid)', buckets.hc_missed, (x) =>
  `  #${x.i}  ${JSON.stringify(x.query)}\n      ref: ${x.refErrors.join(' | ')}`);
show('HC EXTRA (reference valid, HC invalid)', buckets.hc_extra, (x) =>
  `  #${x.i}  ${JSON.stringify(x.query)}\n      hc:  ${x.hcErrors.join(' | ')}`);
show('PARSE DISAGREE', buckets.parse_disagree, (x) =>
  `  #${x.i}  ${JSON.stringify(x.query)}\n      ref=${JSON.stringify(x.node)} hc=${JSON.stringify(x.hc)}`);
