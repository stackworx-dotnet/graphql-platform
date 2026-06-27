// AST-mutation fuzzer. Produces parseable-but-likely-invalid documents that target
// specific validation rules, so the differential focuses on VALIDATION (not syntax).
// Usage: node fuzz.mjs [--count 5000] [--seed 1] [--seeds seeds.json] [--out corpus.jsonl]
import { readFileSync, writeFileSync } from 'node:fs';
import { parse, print, visit, Kind } from 'graphql';

const args = process.argv.slice(2);
const opt = (name, def) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : def; };
const N = parseInt(opt('--count', '5000'), 10);
const seedArg = opt('--seed', null);
const seedsPath = opt('--seeds', new URL('./seeds.json', import.meta.url).pathname);
const outPath = opt('--out', new URL('./corpus.jsonl', import.meta.url).pathname);

let rng = Math.random;
if (seedArg !== null) {
  let s = (parseInt(seedArg, 10) >>> 0) || 1;
  rng = () => { s = (s + 0x6d2b79f5) | 0; let t = Math.imul(s ^ (s >>> 15), 1 | s); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
const ri = (n) => Math.floor(rng() * n);
const pick = (a) => a[ri(a.length)];
const rstr = (p) => p + ri(1e6).toString(36);
const NAME = (v) => ({ kind: Kind.NAME, value: v });

const BASE = [
  '{ dog { name barkVolume } }',
  '{ dog { name(surname: true) nickname } }',
  'query Q($c: Boolean) { dog { isHouseTrained(atOtherHomes: $c) } }',
  'query Q($cmd: DogCommand) { dog { doesKnowCommand(dogCommand: $cmd) } }',
  '{ complicatedArgs { multipleReqs(req1: 1, req2: 2) } }',
  '{ complicatedArgs { enumArgField(enumArg: BROWN) } }',
  '{ complicatedArgs { complexArgField(complexArg: { requiredField: true }) } }',
  '{ complicatedArgs { stringListArgField(stringListArg: ["a", "b"]) } }',
  '{ catOrDog { ... on Dog { name barkVolume } ... on Cat { meows } } }',
  'fragment F on Dog { name barkVolume } { dog { ...F } }',
  '{ human { name pets { name } relatives { name } } }',
  'mutation { createDog(name: "rex") { name } }',
  'subscription { newMessage }',
  '{ dog @onField { name } }',
  'query Named @onQuery { dog { name } }',
  '{ pet { name ... on Dog { barkVolume } ... on Cat { meowsVolume } } }',
  '{ dog { name @complex(arg: "x") } }',
];

const seeds = JSON.parse(readFileSync(seedsPath, 'utf8')).map((s) => s.query);

function countKind(doc, kind) { let n = 0; visit(doc, { [kind]: { enter() { n++; } } }); return n; }
function replaceNth(doc, kind, target, fn) {
  let cur = 0, changed = false;
  const nd = visit(doc, { [kind]: { enter(node, key, parent, path, ancestors) {
    if (cur++ === target) { const r = fn(node, { key, parent, path, ancestors }); if (r !== undefined) { changed = true; return r; } }
  } } });
  return changed ? nd : null;
}
function mutKind(doc, kind, fn) { const c = countKind(doc, kind); if (!c) return null; return replaceNth(doc, kind, ri(c), fn); }

const EDGE_VALUES = [
  { kind: Kind.INT, value: '2147483648' },
  { kind: Kind.INT, value: '-2147483649' },
  { kind: Kind.INT, value: '99999999999999999999' },
  { kind: Kind.FLOAT, value: '1e400' },
  { kind: Kind.FLOAT, value: '1.5' },
  { kind: Kind.STRING, value: 'x', block: false },
  { kind: Kind.BOOLEAN, value: true },
  { kind: Kind.NULL },
  { kind: Kind.ENUM, value: 'NOPE' },
  { kind: Kind.LIST, values: [] },
  { kind: Kind.OBJECT, fields: [] },
];

const fieldMutators = [
  (d) => mutKind(d, Kind.FIELD, (n) => ({ ...n, name: NAME(rstr('zfield_')) })),
  (d) => mutKind(d, Kind.ARGUMENT, (n) => ({ ...n, name: NAME(rstr('zarg_')) })),
  (d) => mutKind(d, Kind.FIELD, (n) => (n.arguments && n.arguments.length ? { ...n, arguments: [] } : undefined)),
  (d) => mutKind(d, Kind.ENUM, (n) => ({ ...n, value: rstr('ZENUM_').toUpperCase() })),
  (d) => mutKind(d, Kind.ARGUMENT, (n) => ({ ...n, value: { kind: Kind.INT, value: String(ri(999)) } })),
  (d) => mutKind(d, Kind.ARGUMENT, (n) => ({ ...n, value: { kind: Kind.STRING, value: rstr('s_'), block: false } })),
  // edge-value injection: surfaces ValuesOfCorrectType range/coercion gaps (e.g. Int overflow).
  (d) => mutKind(d, Kind.ARGUMENT, (n) => ({ ...n, value: pick(EDGE_VALUES) })),
  (d) => mutKind(d, Kind.OBJECT_FIELD, (n) => ({ ...n, value: pick(EDGE_VALUES) })),
  (d) => mutKind(d, Kind.VARIABLE, (n) => ({ ...n, name: NAME(rstr('undef_')) })),
  (d) => mutKind(d, Kind.FIELD, (n) => ({ ...n, directives: [...(n.directives || []), { kind: Kind.DIRECTIVE, name: NAME(rstr('zdir_')) }] })),
  (d) => mutKind(d, Kind.FIELD, (n) => ({ ...n, directives: [...(n.directives || []), { kind: Kind.DIRECTIVE, name: NAME('onField') }, { kind: Kind.DIRECTIVE, name: NAME('onField') }] })),
  (d) => mutKind(d, Kind.INLINE_FRAGMENT, (n) => ({ ...n, typeCondition: { kind: Kind.NAMED_TYPE, name: NAME(pick(['Cat', 'Human', 'Int', 'GeoPoint', 'NoSuchType'])) } })),
  (d) => mutKind(d, Kind.FRAGMENT_DEFINITION, (n) => ({ ...n, typeCondition: { kind: Kind.NAMED_TYPE, name: NAME(pick(['Cat', 'Int', 'NoSuchType'])) } })),
  (d) => mutKind(d, Kind.FIELD, (n) => (n.selectionSet ? undefined : { ...n, selectionSet: { kind: Kind.SELECTION_SET, selections: [{ kind: Kind.FIELD, name: NAME(rstr('sub_')) }] } })),
  (d) => mutKind(d, Kind.FIELD, (n) => (n.selectionSet ? { ...n, selectionSet: undefined } : undefined)),
  (d) => mutKind(d, Kind.OPERATION_DEFINITION, (n) => ({ ...n, variableDefinitions: [...(n.variableDefinitions || []), { kind: Kind.VARIABLE_DEFINITION, variable: { kind: Kind.VARIABLE, name: NAME(rstr('unused_')) }, type: { kind: Kind.NAMED_TYPE, name: NAME('Int') } }] })),
];

const docMutators = [
  (d) => { const ops = d.definitions.filter((x) => x.kind === Kind.OPERATION_DEFINITION && x.name); if (!ops.length) return null; return { ...d, definitions: [...d.definitions, pick(ops)] }; },
  (d) => { const anon = { kind: Kind.OPERATION_DEFINITION, operation: 'query', selectionSet: { kind: Kind.SELECTION_SET, selections: [{ kind: Kind.FIELD, name: NAME('dog'), selectionSet: { kind: Kind.SELECTION_SET, selections: [{ kind: Kind.FIELD, name: NAME('name') }] } }] } }; return { ...d, definitions: [...d.definitions, anon] }; },
  (d) => { const frag = { kind: Kind.FRAGMENT_DEFINITION, name: NAME(rstr('Unused_')), typeCondition: { kind: Kind.NAMED_TYPE, name: NAME('Dog') }, selectionSet: { kind: Kind.SELECTION_SET, selections: [{ kind: Kind.FIELD, name: NAME('name') }] } }; return { ...d, definitions: [...d.definitions, frag] }; },
  (d) => { const sp = { kind: Kind.FRAGMENT_SPREAD, name: NAME(rstr('Missing_')) }; let done = false; const nd = visit(d, { [Kind.SELECTION_SET]: { enter(n) { if (!done) { done = true; return { ...n, selections: [...n.selections, sp] }; } } } }); return done ? nd : null; },
];

const ALL = [...fieldMutators, ...docMutators];
const tryParse = (s) => { try { return parse(s); } catch { return null; } };

const corpus = new Set();
for (const q of [...seeds, ...BASE]) corpus.add(q);

let attempts = 0;
while (corpus.size < N && attempts < N * 20) {
  attempts++;
  const base = pick([...BASE, ...seeds]);
  let doc = tryParse(base);
  if (!doc) continue;
  const rounds = 1 + ri(2);
  for (let r = 0; r < rounds; r++) {
    let nd = null;
    try { nd = pick(ALL)(doc); } catch { nd = null; }
    if (nd) doc = nd;
  }
  let printed;
  try { printed = print(doc); } catch { continue; }
  if (printed.trim().length) corpus.add(printed);
}

const arr = [...corpus].slice(0, N);
writeFileSync(outPath, arr.map((q) => JSON.stringify(q)).join('\n') + '\n');
console.error(`wrote ${arr.length} queries to ${outPath} (attempts=${attempts})`);
