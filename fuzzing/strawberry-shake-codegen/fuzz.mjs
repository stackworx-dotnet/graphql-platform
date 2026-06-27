// Fragment-focused differential fuzzer for StrawberryShake codegen.
//
// Generates GraphQL operation documents that are VALID against schema.graphql
// (verified with graphql-js `validate` before being emitted), biased heavily
// toward the constructs that historically broke StrawberryShake codegen (derived
// from the stackworx/relay-extractor normalizations):
//   - aliases on leaf and on object fields
//   - same-type inline fragments (redundant `... on T`)
//   - field merging across inline fragments / fragment spreads
//   - named fragments (incl. spread twice, nested, on interfaces)
//   - inline fragments on interface/union members
//   - @skip/@include on fragments
//
// Output: corpus.jsonl, one JSON-encoded operation string per line. Every line is
// graphql-js-valid, so any StrawberryShake gen/compile failure on a line is a finding.
import { readFileSync, writeFileSync } from 'node:fs';
import {
  buildSchema, parse, print, validate, specifiedRules,
  Kind, TypeInfo, visit, visitWithTypeInfo,
  getNamedType, isCompositeType, isLeafType, isAbstractType, isObjectType, isInterfaceType,
} from 'graphql';

// ---- args ----
const args = Object.fromEntries(
  process.argv.slice(2).reduce((acc, a, i, arr) => {
    if (a.startsWith('--')) acc.push([a.slice(2), arr[i + 1]]);
    return acc;
  }, []),
);
const COUNT = parseInt(args.count ?? '20000', 10);
const SEED = parseInt(args.seed ?? '1', 10);
const OUT = args.out ?? 'corpus.jsonl';
const SCHEMA = args.schema ?? 'schema.graphql';

const schema = buildSchema(readFileSync(SCHEMA, 'utf8'));

// ---- seeded RNG (mulberry32) ----
let _s = SEED >>> 0;
function rnd() {
  _s |= 0; _s = (_s + 0x6d2b79f5) | 0;
  let t = Math.imul(_s ^ (_s >>> 15), 1 | _s);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}
const chance = (p) => rnd() < p;
const pick = (arr) => arr[Math.floor(rnd() * arr.length)];
let _frag = 0;
const fragName = () => `F${(_frag++).toString(36)}_${Math.floor(rnd() * 1e6).toString(36)}`;
const aliasName = () => `a${Math.floor(rnd() * 1e6).toString(36)}`;

// ---- base operations (all valid; exercise entities/interfaces/unions/connections) ----
const BASE = [
  `query Me { me { id name email createdAt avatar { url width height } } }`,
  `query NodeQ { node(id: "1") { id } }`,
  `query PetQ { pet { __typename } }`,
  `query UsersQ { users(first: 2) { edges { node { id name avatar { url } } cursor } pageInfo { hasNextPage endCursor } totalCount } }`,
  `query AnimalQ { animal { id name owner { id name } } }`,
  `query SearchQ { search(term: "x") { __typename } }`,
  `query FriendsQ { me { id friends(first: 1) { edges { node { id name bestFriend { id name avatar { url } } } } } } }`,
  `query PetsQ { me { id pets { __typename } } }`,
  `query NodeImplQ { node(id: "1") { id ... on User { name } } }`,
  `query DeepImg { me { avatar { url thumbnail(size: 2) { url width } } } }`,
];

// helper: list field names of a (object/interface) type
function fieldNames(type) {
  const named = getNamedType(type);
  if (isObjectType(named) || isInterfaceType(named)) return Object.keys(named.getFields());
  return [];
}
function leafFieldNames(type) {
  const named = getNamedType(type);
  if (!(isObjectType(named) || isInterfaceType(named))) return [];
  const fields = named.getFields();
  return Object.keys(fields).filter(n => isLeafType(getNamedType(fields[n].type)) && fields[n].args.length === 0);
}
function fieldNode(name, sub) {
  return { kind: Kind.FIELD, name: { kind: Kind.NAME, value: name }, selectionSet: sub };
}
function inlineFrag(typeName, selections) {
  return {
    kind: Kind.INLINE_FRAGMENT,
    typeCondition: typeName ? { kind: Kind.NAMED_TYPE, name: { kind: Kind.NAME, value: typeName } } : undefined,
    selectionSet: { kind: Kind.SELECTION_SET, selections },
  };
}
const skipInclude = () => ({
  kind: Kind.DIRECTIVE,
  name: { kind: Kind.NAME, value: chance(0.5) ? 'skip' : 'include' },
  arguments: [{
    kind: Kind.ARGUMENT,
    name: { kind: Kind.NAME, value: 'if' },
    value: { kind: Kind.BOOLEAN, value: chance(0.5) },
  }],
});

// ---- mutators: each takes (doc) and returns a new doc; rely on validate() to discard bad ones ----

// M1: wrap a field's selection set in a same-type inline fragment `... on <fieldType>`
function mutWrapSameType(doc) {
  const ti = new TypeInfo(schema);
  return visit(doc, visitWithTypeInfo(ti, {
    Field(node) {
      if (!node.selectionSet || !chance(0.3)) return undefined;
      const named = getNamedType(ti.getType());
      if (!(isObjectType(named) || isInterfaceType(named))) return undefined;
      return { ...node, selectionSet: { kind: Kind.SELECTION_SET, selections: [inlineFrag(named.name, node.selectionSet.selections)] } };
    },
  }));
}

// M2: split a selection set into two same-type inline fragments
function mutSplitInline(doc) {
  const ti = new TypeInfo(schema);
  return visit(doc, visitWithTypeInfo(ti, {
    SelectionSet(node) {
      if (!chance(0.25)) return undefined;
      const parent = getNamedType(ti.getParentType());
      if (!(isObjectType(parent) || isInterfaceType(parent))) return undefined;
      const fields = node.selections.filter(s => s.kind === Kind.FIELD);
      if (fields.length < 2) return undefined;
      const mid = Math.max(1, Math.floor(fields.length / 2));
      const a = fields.slice(0, mid), b = fields.slice(mid);
      const others = node.selections.filter(s => s.kind !== Kind.FIELD);
      return { kind: Kind.SELECTION_SET, selections: [inlineFrag(parent.name, a), inlineFrag(parent.name, b), ...others] };
    },
  }));
}

// M3: duplicate an object/interface field with a different single sub-field (mergeable)
function mutDupMergeable(doc) {
  const ti = new TypeInfo(schema);
  return visit(doc, visitWithTypeInfo(ti, {
    Field(node) {
      if (!node.selectionSet || node.arguments?.length || !chance(0.25)) return undefined;
      const named = getNamedType(ti.getType());
      const all = fieldNames(named);
      if (all.length === 0) return undefined;
      const present = new Set(node.selectionSet.selections.filter(s => s.kind === Kind.FIELD).map(f => f.name.value));
      const candidates = all.filter(n => !present.has(n));
      if (candidates.length === 0) return undefined;
      const extra = pick(candidates);
      const extraDef = getNamedType(named).getFields()[extra];
      if (extraDef.args.length > 0) return undefined; // keep it argument-free for simplicity
      const sub = isLeafType(getNamedType(extraDef.type)) ? undefined
        : { kind: Kind.SELECTION_SET, selections: [fieldNode(pick(leafFieldNames(extraDef.type) ?? ['__typename']) ?? '__typename')] };
      const clone = { ...node, selectionSet: { kind: Kind.SELECTION_SET, selections: [fieldNode(extra, sub)] } };
      return [node, clone]; // emit original + duplicate sibling
    },
  }));
}

// M4: add an alias to a leaf field
function mutAliasLeaf(doc) {
  const ti = new TypeInfo(schema);
  return visit(doc, visitWithTypeInfo(ti, {
    Field(node) {
      if (node.selectionSet || node.alias || node.name.value.startsWith('__') || !chance(0.25)) return undefined;
      return { ...node, alias: { kind: Kind.NAME, value: aliasName() } };
    },
  }));
}

// M5: add an aliased duplicate of an existing leaf field (alias: field next to field)
function mutAliasDupLeaf(doc) {
  const ti = new TypeInfo(schema);
  return visit(doc, visitWithTypeInfo(ti, {
    SelectionSet(node) {
      if (!chance(0.2)) return undefined;
      const leaf = node.selections.find(s => s.kind === Kind.FIELD && !s.selectionSet && !s.name.value.startsWith('__'));
      if (!leaf) return undefined;
      const dup = { kind: Kind.FIELD, alias: { kind: Kind.NAME, value: aliasName() }, name: { kind: Kind.NAME, value: leaf.name.value } };
      return { ...node, selections: [...node.selections, dup] };
    },
  }));
}

// M6: extract a field's selection set into a named fragment and spread it
function mutExtractFragment(doc) {
  const ti = new TypeInfo(schema);
  const newFrags = [];
  const out = visit(doc, visitWithTypeInfo(ti, {
    Field(node) {
      if (!node.selectionSet || !chance(0.18)) return undefined;
      const named = getNamedType(ti.getType());
      if (!isCompositeType(named)) return undefined;
      const name = fragName();
      newFrags.push({
        kind: Kind.FRAGMENT_DEFINITION,
        name: { kind: Kind.NAME, value: name },
        typeCondition: { kind: Kind.NAMED_TYPE, name: { kind: Kind.NAME, value: named.name } },
        selectionSet: node.selectionSet,
      });
      return { ...node, selectionSet: { kind: Kind.SELECTION_SET, selections: [{ kind: Kind.FRAGMENT_SPREAD, name: { kind: Kind.NAME, value: name } }] } };
    },
  }));
  if (newFrags.length === 0) return out;
  return { ...out, definitions: [...out.definitions, ...newFrags] };
}

// M7: spread an existing fragment a second time within a compatible selection set
function mutSpreadTwice(doc) {
  const spreads = [];
  visit(doc, { FragmentSpread(n) { spreads.push(n.name.value); } });
  if (spreads.length === 0) return doc;
  let done = false;
  return visit(doc, {
    SelectionSet(node) {
      if (done || !chance(0.4)) return undefined;
      const s = node.selections.find(x => x.kind === Kind.FRAGMENT_SPREAD);
      if (!s) return undefined;
      done = true;
      return { ...node, selections: [...node.selections, { kind: Kind.FRAGMENT_SPREAD, name: { kind: Kind.NAME, value: s.name.value } }] };
    },
  });
}

// M8: under an abstract-typed selection set, add `... on <member> { <leaf> }`
function mutInlineImplementer(doc) {
  const ti = new TypeInfo(schema);
  return visit(doc, visitWithTypeInfo(ti, {
    SelectionSet(node) {
      if (!chance(0.3)) return undefined;
      const parent = getNamedType(ti.getParentType());
      if (!isAbstractType(parent)) return undefined;
      const possible = schema.getPossibleTypes(parent);
      if (!possible.length) return undefined;
      const impl = pick(possible);
      const leaves = leafFieldNames(impl);
      const chosen = leaves.length ? pick(leaves) : '__typename';
      return { ...node, selections: [...node.selections, inlineFrag(impl.name, [fieldNode(chosen)])] };
    },
  }));
}

// M9: sprinkle __typename (sometimes aliased)
function mutTypename(doc) {
  return visit(doc, {
    SelectionSet(node) {
      if (!chance(0.2)) return undefined;
      const has = node.selections.some(s => s.kind === Kind.FIELD && s.name.value === '__typename' && !s.alias);
      if (has) return undefined;
      const tn = chance(0.5)
        ? { kind: Kind.FIELD, alias: { kind: Kind.NAME, value: aliasName() }, name: { kind: Kind.NAME, value: '__typename' } }
        : fieldNode('__typename');
      return { ...node, selections: [tn, ...node.selections] };
    },
  });
}

// M10: add @skip/@include to inline fragments and fragment spreads
function mutSkipInclude(doc) {
  return visit(doc, {
    InlineFragment(node) {
      if (!chance(0.2)) return undefined;
      return { ...node, directives: [...(node.directives ?? []), skipInclude()] };
    },
    FragmentSpread(node) {
      if (!chance(0.2)) return undefined;
      return { ...node, directives: [...(node.directives ?? []), skipInclude()] };
    },
  });
}

const MUTATORS = [
  mutWrapSameType, mutSplitInline, mutDupMergeable, mutAliasLeaf, mutAliasDupLeaf,
  mutExtractFragment, mutSpreadTwice, mutInlineImplementer, mutTypename, mutSkipInclude,
];

// ---- generation loop ----
const seen = new Set();
const corpus = [];

function emitIfValidNovel(doc) {
  let text;
  try { text = print(doc); } catch { return; }
  if (seen.has(text)) return;
  let parsed;
  try { parsed = parse(text, { noLocation: true }); } catch { return; }
  const errors = validate(schema, parsed, specifiedRules);
  if (errors.length !== 0) return;
  seen.add(text);
  corpus.push(text);
}

// 1) include curated seeds first (validated the same way)
try {
  const seeds = JSON.parse(readFileSync('seeds.json', 'utf8'));
  for (const s of seeds) {
    try { emitIfValidNovel(parse(s.query, { noLocation: true })); } catch { /* skip bad seed */ }
  }
} catch { /* no seeds file */ }

// 2) fuzz
let attempts = 0;
const maxAttempts = COUNT * 40;
while (corpus.length < COUNT && attempts < maxAttempts) {
  attempts++;
  let doc = parse(pick(BASE), { noLocation: true });
  const nMut = 1 + Math.floor(rnd() * 4);
  for (let k = 0; k < nMut; k++) {
    const m = pick(MUTATORS);
    try { doc = m(doc); } catch { /* mutator failed; keep current doc */ }
  }
  emitIfValidNovel(doc);
}

writeFileSync(OUT, corpus.map(q => JSON.stringify(q)).join('\n') + '\n');
console.error(`wrote ${corpus.length} valid docs to ${OUT} (seed=${SEED}, attempts=${attempts})`);
