# StrawberryShake codegen differential fuzzing: findings

**Goal:** find GraphQL operation documents that **pass validation** (graphql-js, the
spec reference) but StrawberryShake either (a) **throws** while building its model,
(b) returns **generation errors**, or (c) emits **C# that does not compile**.

## Method

For each document:

1. **Oracle:** validate against [schema.graphql](./schema.graphql) with graphql-js
   `validate(schema, parse(q), specifiedRules)`. Only documents the oracle declares
   **valid** are kept, so every corpus line is spec-valid by construction.
2. **Generate:** run StrawberryShake's real generator in-process,
   `CSharpGenerator.Generate(clientModel, settings)` (same path the
   `dotnet-graphql` tool uses), building the `ClientModel` exactly as the generator
   test harness does (`SchemaHelper.Load` + `DocumentAnalyzer.Analyze`).
3. **Compile:** Roslyn-compile the generated C# with the test project's own
   `CSharpCompiler` (all StrawberryShake runtime assemblies referenced), keeping
   error-severity diagnostics.

A **finding** is any valid document whose result is `analyze_threw`, `gen_threw`,
`gen_error`, or `compile_error`. Harness: [HcCodegenRunner](./HcCodegenRunner) (C#),
[fuzz.mjs](./fuzz.mjs) (generator), [node-runner.mjs](./node-runner.mjs) (oracle),
[report.mjs](./report.mjs) (diff). All runs are seeded and reproducible.

**Corpus:** 26 curated seeds + ~12k seeded AST-mutation documents, biased toward the
constructs the [stackworx/relay-extractor](https://github.com/stackworx/relay-extractor)
had to normalize before StrawberryShake could consume a real Relay project: aliases on
leaf/object fields, same-type inline fragments, field merging across inline fragments
and fragment spreads, named fragments (spread twice / nested / on interfaces), and
inline fragments on interface/union members. That tool's `transform.ts` names
StrawberryShake explicitly (`removeAliasesOnLeafFields`: *"to improve compatibility
with Strawberry Shake"*; `flattenInlineFragmentsSameType` + `dedupeSelectionSet`),
which is what pointed this fuzzer at fragments.

**Full-corpus result:** of **11,800** distinct graphql-js-valid documents, StrawberryShake
failed on **122** (~1%), across exactly **two** signatures: **118** hit Finding 2 (CS0528,
non-compiling output) and **4** hit Finding 1 (generator throw). No other failure
signature survived once the `DateTime` scalar binding (see "Not a finding" below) was
corrected, and StrawberryShake's parser never disagreed with graphql-js (no parse
mismatch). Both findings minimize to one-line repros (next sections).

---

## Finding 1 — a field selected both directly and via a fragment throws during generation

When a **composite** field is selected **directly** at a selection level **and also
inside a fragment** (an inline fragment of the same type, or a named fragment spread)
at that same level, StrawberryShake throws while mapping result types:

```
System.InvalidOperationException: Could not find an output type for the specified field syntax.
```

This is a hard generator crash, not a reported error, so no client is produced at all.

| operation (all are graphql-js **valid**) | StrawberryShake |
| --- | --- |
| `query Q { me { avatar { url } ... on User { avatar { width } } } }` | **THROWS** |
| `query Q { me { avatar { url } ... on User { avatar { url } } } }` (identical subselection) | **THROWS** |
| `query Q { me { avatar { url } ...UF } } fragment UF on User { avatar { width } }` (named spread) | **THROWS** |
| `query Q { me { bestFriend { id } ... on User { bestFriend { name } } } }` (nested) | **THROWS** |
| `query Q { me { avatar { url } avatar { width } } }` (both direct, no fragment) | ok |
| `query Q { me { ... on User { avatar { url } } ... on User { avatar { width } } } }` (both in fragments) | ok |
| `query Q { me { x: avatar { url } x: avatar { width } } }` (aliased, both direct) | ok |

The distinguishing factor is the **mix**: direct-and-direct merges fine, and
fragment-and-fragment merges fine, but **direct-field meets fragment-wrapped-field**
of the same composite field throws (even when the two subselections are identical, so
it is not a shape conflict).

### Root cause

`src/StrawberryShake/CodeGeneration/src/CodeGeneration/Analyzers/Models/OperationModel.cs:83-101`
(`TryGetFieldResultType`) resolves a field's result type by **reference-equality on the
selection-set node**:

```csharp
fieldType = OutputTypes.FirstOrDefault(
    t => t.IsInterface && t.SelectionSet == selectionSetNode); // reference equality
return fieldType is not null;
```

When the same composite field appears both directly and inside a fragment, the
analyzer merges them into a synthesized selection set whose node is not the one
registered as an interface `OutputType`, so the lookup returns `null`. The caller
`TypeDescriptorMapper.GetFieldTypeDescriptor`
(`.../Mappers/TypeDescriptorMapper.cs:467-488`) then throws
`"Could not find an output type for the specified field syntax."`. The
relay-extractor's `flattenInlineFragmentsSameType` + `mergeSelectionSets` worked
around exactly this by collapsing the field and the fragment into one direct selection
before handing the document to StrawberryShake.

---

## Finding 2 — spreading the same named fragment twice emits non-compiling C# (CS0528)

Spreading the **same named fragment two or more times in one selection set** is valid
GraphQL (the spreads merge), but StrawberryShake emits a result interface that lists
the fragment's generated interface twice in its base list:

```
error CS0528: 'IUF' is already listed in interface list
```

Generation "succeeds" (no reported error) but the output **does not compile**.

| operation (all are graphql-js **valid**) | StrawberryShake |
| --- | --- |
| `query Q { me { ...UF ...UF } } fragment UF on User { id name }` | **CS0528** |
| `query Q { me { id ...UF ...UF } } fragment UF on User { id name }` | **CS0528** |
| `query Q { node(id: "1") { ...NF ...NF } } fragment NF on Node { id }` (interface) | **CS0528** |
| `query Q { me { ...UF ...UF ... on User { email } } } fragment UF on User { id name }` | **CS0528** |
| `query Q { me { ...UF } } fragment UF on User { id name }` (single) | ok |
| `query Q { me { ...UF ...UG } } fragment UF on User { id name } fragment UG on User { email }` (two different) | ok |
| `query Q { me { ...UF bestFriend { ...UF } } } fragment UF on User { id name }` (different levels) | ok |
| `query Q { pet { ...DF ...DF } } fragment DF on Dog { name }` (union member spread) | ok |

Trigger is specifically the **same** fragment spread **≥2×** in the **same** selection
set. Distinct fragments, the same fragment at different levels, and the same fragment
spread twice onto a union member do not trip it. The relay-extractor's
`dedupeSelectionSet` (collapsing duplicate selections) avoided this.

---

## Finding 3 (minor) — anonymous operations are rejected

`{ me { id name } }` and `query { me { id } }` are valid GraphQL but StrawberryShake
throws `CodeGeneratorException: All operations must be named.` during analysis. This is
a long-standing, effectively-documented StrawberryShake constraint (it names a class
per operation), listed here only because it is technically a valid-but-ungeneratable
document and it masked Findings 1/2 in early minimization (anonymous probes all failed
here first).

---

## Not a finding — custom-scalar `@serializationType` binding (CS1503)

An early [schema.extensions.graphql](./schema.extensions.graphql) bound the custom
`DateTime` scalar with `@serializationType(name: "System.DateTime")`. That is wrong:
`DateTime`'s wire value is a string, so the generated builder passed a `string` where a
`System.DateTime` was expected, and the output failed to compile with
`CS1503: Argument 1: cannot convert from 'string' to 'System.DateTime'`. It reproduced
on a trivial **fragment-free** query (`query Q { me { createdAt } }`), which is what
flagged it as a harness misconfiguration rather than a StrawberryShake bug. Left as a
bare (string-backed) scalar, `DateTime` generates compiling code. Excluded from the
findings (it accounted for the `CS1503` signature seen in the first full run).

## Related upstream issues (open)

Fragments are already a known weak area; searching `ChilliCream/graphql-platform`
surfaced a cluster, though **none matches Finding 1 or 2 exactly**:

- [#8063](https://github.com/ChilliCream/graphql-platform/issues/8063) — *Order of
  Inline Fragments Causes Missing Data* (runtime: missing data, has a repro repo).
- [#5992](https://github.com/ChilliCream/graphql-platform/issues/5992) — *Generated
  union type classes are empty when using fragments* (repro-validated).
- [#6873](https://github.com/ChilliCream/graphql-platform/issues/6873) — *Generating
  predictable model classes* (the same fragment generates a separate model per
  operation).
- [#6415](https://github.com/ChilliCream/graphql-platform/issues/6415) — *Unknown
  interface types in result list throws exception* (runtime deserialization).

Findings 1 and 2 are distinct (a generator throw and a non-compiling-output bug,
respectively, both with one-line repros) and appear unreported.

---

## Reproduce

```bash
cd fuzzing/strawberry-shake-codegen
npm install
dotnet build -c Release HcCodegenRunner/HcCodegenRunner.csproj

# the two findings, minimally:
node min.mjs                       # writes min.jsonl + prints graphql-js validity (all VALID)
dotnet HcCodegenRunner/bin/Release/net10.0/HcCodegenRunner.dll \
  schema.graphql schema.extensions.graphql min.jsonl
# A1/A4/A5/A7 -> genThrew; B1/B4/B6/B8 -> compiled:false CS0528; C3 -> analyzeThrew

# full fuzz:
node fuzz.mjs --count 6000 --seed 2 --out corpus.jsonl
node node-runner.mjs schema.graphql corpus.jsonl > node-results.jsonl
dotnet HcCodegenRunner/bin/Release/net10.0/HcCodegenRunner.dll \
  schema.graphql schema.extensions.graphql corpus.jsonl > hc-results.jsonl
node report.mjs corpus.jsonl node-results.jsonl hc-results.jsonl out.json
```
