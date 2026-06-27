// Minimization probes: hand-crafted variants around each finding to pin the exact
// trigger boundary. Writes min.jsonl (JSON-encoded queries) + min-labels.json, and
// prints graphql-js validity per probe so we never misattribute an invalid doc.
import { writeFileSync, readFileSync } from 'node:fs';
import { buildSchema, parse, validate, specifiedRules } from 'graphql';

const schema = buildSchema(readFileSync('schema.graphql', 'utf8'));

// NOTE: StrawberryShake requires NAMED operations (anonymous `{ ... }` throws
// "All operations must be named" at analyze time), so every probe is named.
const probes = [
  // --- Finding A: field merged across same-type inline fragment ---
  ['A1 baseline (field+inline, diff subsel)', 'query A1 { me { avatar { url } ... on User { avatar { width } } } }'],
  ['A2 plain duplicate field (no fragment)', 'query A2 { me { avatar { url } avatar { width } } }'],
  ['A3 both inside inline frags', 'query A3 { me { ... on User { avatar { url } } ... on User { avatar { width } } } }'],
  ['A4 identical subselection merge', 'query A4 { me { avatar { url } ... on User { avatar { url } } } }'],
  ['A5 via named fragment spread', 'query A5 { me { avatar { url } ...UF } } fragment UF on User { avatar { width } }'],
  ['A6 split scalars same type (no nested merge)', 'query A6 { node(id: "1") { ... on User { name } ... on User { email } } }'],
  ['A7 nested object field merge (bestFriend)', 'query A7 { me { bestFriend { id } ... on User { bestFriend { name } } } }'],
  ['A8 deep (connection) merge', 'query A8 { me { friends(first: 1) { edges { node { avatar { url } ... on User { avatar { width } } } } } } }'],
  ['A9 aliased object merge (x: avatar)', 'query A9 { me { x: avatar { url } x: avatar { width } } }'],
  ['A10 scalar dup merge (no subsel)', 'query A10 { me { name name } }'],

  // --- Finding B: same named fragment spread twice ---
  ['B1 baseline same frag twice', 'query B1 { me { ...UF ...UF } } fragment UF on User { id name }'],
  ['B2 single spread (control)', 'query B2 { me { ...UF } } fragment UF on User { id name }'],
  ['B3 two different frags', 'query B3 { me { ...UF ...UG } } fragment UF on User { id name } fragment UG on User { email }'],
  ['B4 two spreads + field', 'query B4 { me { id ...UF ...UF } } fragment UF on User { id name }'],
  ['B5 same frag at two levels', 'query B5 { me { ...UF bestFriend { ...UF } } } fragment UF on User { id name }'],
  ['B6 same frag twice on interface', 'query B6 { node(id: "1") { ...NF ...NF } } fragment NF on Node { id }'],
  ['B7 same frag twice on union member', 'query B7 { pet { ...DF ...DF } } fragment DF on Dog { name }'],
  ['B8 spread twice + inline same type', 'query B8 { me { ...UF ...UF ... on User { email } } } fragment UF on User { id name }'],

  // --- controls ---
  ['C1 trivial control', 'query C1 { me { id name } }'],
  ['C2 named fragment once on union', 'query C2 { pet { ...DF } } fragment DF on Dog { name }'],
  ['C3 anonymous op (valid GQL, SS rejects)', '{ me { id name } }'],
];

const labels = [];
const lines = [];
for (const [label, query] of probes) {
  let valid = false, err = '';
  try {
    const errs = validate(schema, parse(query, { noLocation: true }), specifiedRules);
    valid = errs.length === 0;
    if (!valid) err = errs.map(e => e.message).join(' | ');
  } catch (e) { err = 'PARSE: ' + e.message; }
  labels.push({ label, query, oracleValid: valid, oracleErr: err });
  lines.push(JSON.stringify(query));
  console.log(`${valid ? 'VALID  ' : 'INVALID'}  ${label}${valid ? '' : '  <-- ' + err}`);
}

writeFileSync('min.jsonl', lines.join('\n') + '\n');
writeFileSync('min-labels.json', JSON.stringify(labels, null, 2));
console.log(`\nwrote ${lines.length} probes to min.jsonl`);
