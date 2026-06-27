# GraphQL validation differential fuzzer

Differentially fuzz GraphQL document **validation** between the spec reference
implementation (graphql-js) and HotChocolate, to find documents that graphql-js
rejects as invalid but HotChocolate accepts (HC missed a spec violation).

See [FINDINGS.md](./FINDINGS.md) for what it has turned up.

## Pieces

| File | Role |
| --- | --- |
| `schema.graphql` | Shared schema (graphql-js validation harness, adapted). Loaded by both engines. |
| `seeds.json` | 36 curated known-invalid documents (verified against graphql-js). |
| `hard.json` | 35 hand-written hard cases targeting rules implementations classically diverge on. |
| `fuzz.mjs` | AST-mutation fuzzer. Mutates valid bases + seeds into parseable-but-likely-invalid docs targeting specific rules. |
| `node-runner.mjs` | Reference runner: `graphql-js` `validate(schema, parse(q), specifiedRules)`. |
| `HcValidationRunner/` | HotChocolate runner: `DocumentValidatorBuilder.New().AddDefaultRules()` against the schema-first schema. |
| `diff.mjs` | Compares the two result files; reports `hc_missed` / `hc_extra` / `parse_disagree`. |

## I/O contract

- **Corpus** (`corpus.jsonl`): one JSON-encoded query string per line.
- **Results** (`*-results.jsonl`): one record per corpus line, aligned by index:
  `{ "i", "parseError", "valid", "errorCount", "messages": [..] }`.
- **Diff categories**: `hc_missed` (reference invalid, HC valid — the prime signal),
  `hc_extra` (reference valid, HC invalid), `parse_disagree` (one parser errored, the other didn't).

## Run

```bash
npm install   # graphql@17
dotnet build -c Release HcValidationRunner/HcValidationRunner.csproj

node fuzz.mjs --count 15000 --seed 2
node node-runner.mjs schema.graphql corpus.jsonl > node-results.jsonl
dotnet HcValidationRunner/bin/Release/net10.0/HcValidationRunner.dll schema.graphql corpus.jsonl > hc-results.jsonl
node diff.mjs corpus.jsonl node-results.jsonl hc-results.jsonl out.json
```

## Parity notes

- Reference: graphql-js **v17.0.1**, `specifiedRules`. HotChocolate: full default rule set.
- HC schema-first build enables defer/stream and registers `GeoPoint` as a permissive
  scalar (`AnyType`) so a custom scalar with no `parseLiteral` matches graphql-js behaviour.
- The schema omits `@oneOf` to keep both parsers identical.
- graphql-js v17 has defer/stream + max-introspection-depth rules; if comparing against a
  HotChocolate that lacks any of these, exclude them from `specifiedRules` to avoid noise.
