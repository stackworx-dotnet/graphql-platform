# Differential validation findings: graphql-js vs HotChocolate

Method: validate the same GraphQL documents against the same schema
([schema.graphql](./schema.graphql)) with both the spec reference validator
(graphql-js v17.0.1, `validate(schema, parse(q), specifiedRules)`) and
HotChocolate's full default rule set (`DocumentValidatorBuilder.New().AddDefaultRules()`),
then compare. The signal we hunt for is **reference reports invalid, HotChocolate
reports valid** (HC missed a spec violation).

Corpus: 36 curated seeds + 35 hand-written hard cases + ~15k AST-mutation fuzz
documents (seeded, reproducible). Across all of it HotChocolate's validation
matched graphql-js except for the items below.

## Finding 1 — `Int` literals are not range-checked (broad)

HotChocolate validates that an `Int` argument receives an integer *kind* (it
rejects `[1,2]`, `{a:1}`, `"x"` for `Int`), but it does **not** check the 32-bit
signed range that the GraphQL `Int` scalar mandates. graphql-js rejects
out-of-range integer literals during validation; HotChocolate accepts them.

Reproduction (all reported VALID by HotChocolate, INVALID by graphql-js):

```graphql
{ complicatedArgs { intArgField(intArg: 2147483648) } }            # Int32 max + 1
{ complicatedArgs { intArgField(intArg: -2147483649) } }           # Int32 min - 1
{ complicatedArgs { intArgField(intArg: 99999999999999999999) } }  # far out of range
{ complicatedArgs { multipleReqs(req1: 1, req2: 2147483648) } }    # non-null arg position
query ($v: Int = 9999999999) { complicatedArgs { intArgField(intArg: $v) } }  # variable default
```

graphql-js message: `Int cannot represent non 32-bit signed integer value: <n>`.

Boundary confirmed: `2147483647` and `-2147483648` are accepted by both (valid);
the very next values (`±` one past the Int32 edge) are where they diverge. The
gap applies in every literal position tested: direct argument, non-null argument,
and variable default value.

Note: HotChocolate would still fail these at execution-time coercion. This is a
**validation-completeness** gap (the document is declared valid when the spec
says it is not), not an execution-correctness bug.

## Finding 2 — a list literal where an input object is expected is not rejected

```graphql
{ complicatedArgs { complexArgField(complexArg: []) } }
```

graphql-js: `Expected value of type "ComplexInput" to be an object, found: [].`
HotChocolate: VALID (it also does not report the otherwise-missing required field
`ComplexInput.requiredField`).

This is narrow: every other shape mismatch we probed is caught by HotChocolate
(`5`, `"x"`, `true`, `BROWN` for an input object; `[1,2]` / `{a:1}` for a scalar;
`{a:1}` for a list). Only a **list** value supplied for an input-object-typed
argument slips through.

## Not a finding — parser directive cap (behavioral difference)

```graphql
{ dog { name @onField @onField @onField @onField @onField } }
```

graphql-js parses this and reports validation errors (duplicate directive).
HotChocolate's **parser** aborts: `A location in the GraphQL document contains
more than 4 directives. Parsing aborted.` This is an intentional DoS hardening at
the parser layer, not a validation miss, so it surfaces as `parse_disagree`, not
`hc_missed`. Documented for completeness.

## Reproduce

```bash
cd fuzzing/graphql-validation
npm install
# targeted hard cases:
node --input-type=module -e "import {readFileSync,writeFileSync} from 'node:fs'; const s=JSON.parse(readFileSync('hard.json','utf8')); writeFileSync('hard-corpus.jsonl', s.map(x=>JSON.stringify(x.query)).join('\n')+'\n')"
node node-runner.mjs schema.graphql hard-corpus.jsonl > node-hard.jsonl
dotnet HcValidationRunner/bin/Release/net10.0/HcValidationRunner.dll schema.graphql hard-corpus.jsonl > hc-hard.jsonl
node diff.mjs hard-corpus.jsonl node-hard.jsonl hc-hard.jsonl
# or fuzz:
node fuzz.mjs --count 15000 --seed 2
node node-runner.mjs schema.graphql corpus.jsonl > node-results.jsonl
dotnet HcValidationRunner/bin/Release/net10.0/HcValidationRunner.dll schema.graphql corpus.jsonl > hc-results.jsonl
node diff.mjs corpus.jsonl node-results.jsonl hc-results.jsonl out.json
```
