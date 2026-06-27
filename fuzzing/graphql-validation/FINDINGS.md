# Differential validation findings: graphql-js vs HotChocolate

Method: validate the same documents against the same schema
([schema.graphql](./schema.graphql)) with the spec reference validator
(graphql-js v17.0.1, `validate(schema, parse(q), specifiedRules)`) and
HotChocolate's full default rule set
(`DocumentValidatorBuilder.New().AddDefaultRules()`), then diff. Prime signal:
**reference reports invalid, HotChocolate reports valid**.

Corpus: 36 curated seeds + ~70 hand-written hard cases ([hard.json](./hard.json),
[hard2.json](./hard2.json)) + ~50k AST-mutation fuzz documents (seeded,
reproducible). Across all of it HotChocolate matched graphql-js except for the two
findings below. No `hc_extra` (HC was never *stricter* than the spec) was observed.

Related HotChocolate issues: searched `ChilliCream/graphql-platform` for `Int`
range / `ValuesOfCorrectType` / input-value / scalar-literal validation — **no
matching issue found**; both findings appear unreported. (The `>4 directives ->
Parsing aborted` behaviour seen in `parse_disagree` is an intentional parser DoS
guard: `ParserOptions.Default.MaxAllowedDirectives = 4`, not a validation gap.)

---

## Finding 1 — `Int` literals are not range-checked (localized miss)

HotChocolate validates the *kind* of an `Int` literal (it rejects `[1,2]`,
`{a:1}`, `"x"` for `Int`) but not the 32-bit signed range the GraphQL `Int`
scalar mandates. graphql-js rejects out-of-range integer literals during
validation; HotChocolate accepts them.

```graphql
{ complicatedArgs { intArgField(intArg: 2147483648) } }            # Int32 max + 1
{ complicatedArgs { intArgField(intArg: -2147483649) } }           # Int32 min - 1
{ complicatedArgs { intArgField(intArg: 99999999999999999999) } }
{ complicatedArgs { multipleReqs(req1: 1, req2: 2147483648) } }    # non-null arg
{ complicatedArgs { nestedArgField(nested: { required: 2147483648 }) } }      # nested input field
{ complicatedArgs { nestedArgField(nested: { required: 1, ints: [1, 2147483648] }) } }  # nested list item
query ($v: Int = 9999999999) { complicatedArgs { intArgField(intArg: $v) } } # variable default
query ($v: NestedInput = { required: 2147483648 }) { complicatedArgs { nestedArgField(nested: $v) } }
```

graphql-js: `Int cannot represent non 32-bit signed integer value: <n>`.

- Boundary is exactly Int32: `2147483647` / `-2147483648` accepted by both; one
  past the edge diverges.
- Holds at every literal position: direct arg, non-null arg, nested input field,
  nested list element, variable default, and nested variable default.
- **Localized**: it does *not* suppress sibling errors. HotChocolate still reports
  a bad enum / unused variable / unknown field alongside the un-flagged Int.
- This is a validation-completeness gap (the document is declared valid when the
  spec says otherwise); execution-time coercion would still fail.

## Finding 2 — a list value where an input object is expected silently aborts validation (error-masking bypass)

When an argument expecting an **input object** is given a **list** literal,
HotChocolate reports the **entire document as valid** and **suppresses every other
validation error in it**.

```graphql
# reference: 2 errors (list-for-input + bad enum). HotChocolate: VALID, 0 errors.
{ complicatedArgs { complexArgField(complexArg: []) enumArgField(enumArg: NOPE) } }

# reference: 2 errors (unused var + list-for-input). HotChocolate: VALID, 0 errors.
query ($u: Int) { complicatedArgs { complexArgField(complexArg: []) } }

# also masked: unknown field, string-for-Int, etc., whenever any input-object arg gets a list.
```

Scope (precisely): the trigger is **(list value) x (input-object type)** only.
Both empty `[]` and non-empty `[{ ... }]` lists fire it. It is NOT triggered by a
scalar given for an input object (`complexArg: 5`), an object given for a list
(`stringListArg: {a:1}`), or a list given for a scalar field inside an input
object (`{ stringField: [] }`) — those each produce a normal, localized error.

### Root cause

`src/HotChocolate/Core/src/Validation/Rules/ValueVisitor.cs:305-325`:

```csharp
protected override ISyntaxVisitorAction Enter(ListValueNode node, DocumentValidatorContext context)
{
    if (context.Types.TryPeek(out var type))
    {
        if (type.NamedType().IsLeafType())  { return Enter((IValueNode)node, context); } // scalar: validated
        if (type.IsListType())              { context.Types.Push(type.ElementType()); return Continue; }
    }
    context.UnexpectedErrorsDetected = true;   // list for an INPUT OBJECT type
    return Break;                              // <-- aborts traversal, NO ReportError
}
```

For a list value whose expected type is an input object (neither leaf nor list)
the visitor sets `UnexpectedErrorsDetected` and returns `Break` **without calling
`context.ReportError(...)`**. `Break` aborts the validation traversal, so no error
is recorded for the malformed value and subsequent nodes/rules are not visited,
leaving the document reported valid. The scalar mismatch path
(`Enter(IValueNode)`, line 335) correctly calls `ReportError`; the list path does
not. Likely fix: report a "value is not of the correct type" error (as the scalar
path does) instead of `Break`-ing.

### Why it matters

A client can append `anyInputObjectArg: []` to an otherwise-invalid (or
abuse-shaped) document and have HotChocolate's validator pass it as valid,
bypassing all other validation rules. Worth confirming downstream behaviour
(execution/coercion) and reporting upstream.

---

## Reproduce

```bash
cd fuzzing/graphql-validation && npm install
dotnet build -c Release HcValidationRunner/HcValidationRunner.csproj

# the two findings, minimally:
printf '%s\n' \
  '"{ complicatedArgs { intArgField(intArg: 2147483648) } }"' \
  '"{ complicatedArgs { complexArgField(complexArg: []) enumArgField(enumArg: NOPE) } }"' > /tmp/repro.jsonl
node node-runner.mjs schema.graphql /tmp/repro.jsonl
dotnet HcValidationRunner/bin/Release/net10.0/HcValidationRunner.dll schema.graphql /tmp/repro.jsonl
# graphql-js: both invalid. HotChocolate: both valid.

# full fuzz:
node fuzz.mjs --count 30000 --seed 3
node node-runner.mjs schema.graphql corpus.jsonl > node-results.jsonl
dotnet HcValidationRunner/bin/Release/net10.0/HcValidationRunner.dll schema.graphql corpus.jsonl > hc-results.jsonl
node diff.mjs corpus.jsonl node-results.jsonl hc-results.jsonl out.json
```
