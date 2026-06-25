# FORK.md - Fork Agent Instructions

> Fork-specific guidance. The root `CLAUDE.md` (upstream) still applies for build,
> code style, and testing conventions. This file adds the fork's mission and workflow.
> It is intended to be short-lived and removed when the fork is retired.

## Mission

This is a **short-lived fork** of [ChilliCream/graphql-platform](https://github.com/ChilliCream/graphql-platform).
We carry a small, curated set of **Strawberry Shake** fixes and one feature ahead of
upstream, and publish them as versioned fork releases. Each concern lives on its own
branch so it stays minimal, rebasable on upstream `main`, individually openable as an
upstream PR, and easy to drop onc3e upstream ships an equivalent.

Goal: **dissolve this fork.** Every tracked item links back to an upstream PR/issue;
when upstream merges an equivalent, retire the branch.

## Remotes

| Remote | Repo | Use |
| --- | --- | --- |
| `origin` | `stackworx-dotnet/graphql-platform` (fork) | Push topic + release branches here. |
| `upstream` | `ChilliCream/graphql-platform` | Sync `main` from here. **Never push.** |

Already configured. Keep `main` a clean mirror of upstream:

```bash
git fetch upstream
git checkout main && git rebase upstream/main   # main tracks upstream, no fork commits
```

## Branching model (two tiers)

**1. Topic branches - one per concern.** This is the unit of work.
- Name to mirror the upstream PR branch when one exists (e.g. `fix/razor-operation-interface`);
  otherwise `fix/<slug>` or `feat/<slug>`.
- Branch from `upstream/main`. Keep the diff minimal and in upstream's style.
- **Prefer cherry-picking the real upstream commits** over re-implementing, when an upstream
  PR exists - it preserves authorship and makes retirement a clean revert/drop.
- One concern = one branch. Never mix unrelated fixes.

**2. Release branches - what we publish.** `release/fork-<version>[.<ext>]`
- `<version>` matches the published fork package version (e.g. `release/fork-16.0.0`).
- Append the `.<ext>` suffix for additional publishes of the same base version
  (e.g. `release/fork-16.0.0.1`, `release/fork-16.0.0.2`).
- Assemble a release branch by merging/cherry-picking the selected topic branches onto the
  matching `upstream` base tag. A release branch is disposable and re-buildable from the
  topic branches.

Push topic and release branches to `origin`. Do not push to `upstream`.

## Per-concern workflow

1. `git fetch upstream && git checkout -b <topic> upstream/main`
2. Make the change (cherry-pick upstream commits if a PR exists).
3. Build the area, run the **filtered** tests, update snapshots from `__mismatch__/` only
   after confirming the diff is intended (see Verification).
4. Push the topic branch to `origin`.
5. At publish time, merge/cherry-pick the chosen topic branches into the
   `release/fork-<version>` branch and push.

## Verification

Run from the repo root. Build/test only the affected area - never the full suite.

```bash
# Generators (the Razor/CSharp code generators)
dotnet build src/StrawberryShake/CodeGeneration/StrawberryShake.CodeGeneration.slnx

# Razor runtime base classes (UseQuery / UseSubscription / DataComponent)
dotnet build src/StrawberryShake/Client/StrawberryShake.Client.slnx

# Targeted tests (filter during iteration)
dotnet test src/StrawberryShake/CodeGeneration/test/CodeGeneration.Razor.Tests/StrawberryShake.CodeGeneration.Razor.Tests.csproj
dotnet test src/StrawberryShake/CodeGeneration/test/CodeGeneration.CSharp.Tests/StrawberryShake.CodeGeneration.CSharp.Tests.csproj
```

- "Done" = compiles **and** the targeted tests pass.
- Snapshot tests use **CookieCrumble**; update from the `__mismatch__/` directory only after
  you understand the diff (ordering changes are common in this area).
- Follow root `CLAUDE.md` C#/test conventions (file-scoped namespaces, braces always, AAA
  markers, snapshot-first, no em dashes, max 5 `Assert.*` per test).

## Tracked work items

### 1. Razor components use the Operation interface type (ready)

- **Upstream:** PR [#9636](https://github.com/ChilliCream/graphql-platform/pull/9636)
  (author `cliedeman`), fixes [#9635](https://github.com/ChilliCream/graphql-platform/issues/9635).
- **Branch:** `fix/razor-operation-interface`.
- **What:** generated `Use*` components inject the operation **interface**
  (`IGetBarsQuery`) instead of the concrete runtime type (`GetBarsQuery`), so components are
  mockable in bUnit. ~3 lines.
- **Files:**
  - `src/StrawberryShake/CodeGeneration/src/CodeGeneration.CSharp/Generators/RazorQueryGenerator.cs`
    (`descriptor.RuntimeType` -> `descriptor.InterfaceType`)
  - `.../Generators/RazorSubscriptionGenerator.cs` (same)
  - `.../test/CodeGeneration.Razor.Tests/__snapshots__/RazorGeneratorTests.Query_And_Mutation.snap`
- **Action:** cherry-pick the upstream PR commit; verify the Razor snapshot.
- **Caveat:** interacts with vnext (#9977), which rewrites these generators - see the
  candidate table.

### 2. Razor components: persisted component state / pre-rendering (new feature)

- **Relates to:** upstream issue [#7209](https://github.com/ChilliCream/graphql-platform/issues/7209).
- **Branch:** `feat/razor-persistent-component-state`.
- **Goal:** generated Blazor components survive the prerender -> interactive boundary using
  `PersistentComponentState`, so the query is not re-executed on the client after a server
  prerender (`InteractiveAuto` / `InteractiveServer`).
- **The showstopper (from #7209):** operation results are **interfaces** (`IGetBarsResult`).
  `PersistentComponentState.PersistAsJson` / `TryTakeFromJson` cannot (de)serialize
  interfaces, and the concrete result classes reference entities in `IEntityStore`, so they
  do not round-trip cleanly either.
- **Recommended approach:** persist the **raw transport JSON** (`data` payload) of the latest
  `IOperationResult`, not the typed result. On interactive init, take the payload back and
  rehydrate it through the existing generated `JsonResultBuilder` -> `IEntityStore` -> typed
  result, then `Subscribe(Operation.Watch(...))`. This reuses the existing deserialization
  path and avoids interface serialization entirely. First verify the raw `JsonDocument` is
  still reachable from the result; if not, capture it in the result builder / operation store.
- **Touch points:**
  - Runtime: `src/StrawberryShake/Client/src/Razor/UseQuery.cs` (and `UseSubscription.cs`,
    `DataComponent.cs`) - add **opt-in** `PersistentComponentState` wiring: register-on-persisting
    to save the latest payload during prerender; on the first interactive init, take + seed the
    store before subscribing.
  - Generators: `RazorQueryGenerator.cs` / `RazorSubscriptionGenerator.cs` - emit the wiring
    (inject `PersistentComponentState`, a stable persistence key per operation + args, opt-in
    flag). Keep generated output minimal and behind an opt-in so non-prerender apps are
    unaffected.
  - Tests: update `CodeGeneration.Razor.Tests` golden snapshots; add a focused test for the
    persisted-state wiring.
- **Design questions to settle before coding:** persistence key scheme (operation name +
  serialized args); opt-in mechanism (component parameter vs `.graphqlrc.json` generator
  setting); behavior when the payload is absent; subscription/SSE interaction (see #6944).
- **Build against current `main`, not vnext** (#9977), for fork longevity.

## Candidate issues & PRs

Triage table. Set **Address?** to `Yes` / `No` per row; anything marked `Yes` gets a topic
branch. The two tracked items above are pre-marked. PRs are the realistic cherry-pick units
(implementation already exists); issues without a PR are larger efforts.

| Address? | ID | Type | Title | Author | Branch / notes |
| --- | --- | --- | --- | --- | --- |
| **Yes** | [#9636](https://github.com/ChilliCream/graphql-platform/pull/9636) | PR | Use Operation interface type instead of runtime type (fixes #9635) | cliedeman | `fix/razor-operation-interface` - tracked item 1 |
| **Yes** | [#7209](https://github.com/ChilliCream/graphql-platform/issues/7209) | Issue | Make StrawberryShake play nicely with Blazor `PersistentComponentState` | nloum | `feat/razor-persistent-component-state` - tracked item 2 |
| ☐ | [#9476](https://github.com/ChilliCream/graphql-platform/pull/9476) | PR | Fix `@rename` directive on input type fields | waldemarsson | – |
| ☐ | [#9275](https://github.com/ChilliCream/graphql-platform/pull/9275) | PR | Preserve unset SS input field state (fixes #8325) | michaelstaib | – |
| ☐ | [#8531](https://github.com/ChilliCream/graphql-platform/pull/8531) | PR (draft) | Add "Unknown Enum" support | N-Olbert | – |
| ☐ | [#7878](https://github.com/ChilliCream/graphql-platform/pull/7878) | PR | `AddScopedXClient` | repne | – |
| ☐ | [#7171](https://github.com/ChilliCream/graphql-platform/pull/7171) | PR | Prevent `ArgumentNullException` when `@include` is false (issue #6616) | grounzero | – |
| ☐ | [#6001](https://github.com/ChilliCream/graphql-platform/pull/6001) | PR | Mutation example: Blazor + Strawberry Shake (sample/docs) | DaveHOnCode | – |
| ☐ | [#9635](https://github.com/ChilliCream/graphql-platform/issues/9635) | Issue | Razor components use RuntimeType instead of InterfaceType | cliedeman | addressed by #9636 |
| ☐ | [#6944](https://github.com/ChilliCream/graphql-platform/issues/6944) | Issue | Subscriptions on Blazor WASM do not stream over SSE (confirmed bug) | - | adjacent to item 2 (subscriptions only) |
| ☐ | [#4951](https://github.com/ChilliCream/graphql-platform/issues/4951) | Issue | Expose a bindable `Loading` property on generated Razor components | - | Razor/Blazor adjacent |
| ☐ | [#6365](https://github.com/ChilliCream/graphql-platform/issues/6365) | Issue | `UseQuery<T>` re-evaluated on all page events | - | Razor runtime adjacent |
| _watch_ | [#9977](https://github.com/ChilliCream/graphql-platform/pull/9977) | PR | **StrawberryShake vnext** - re-platforms the generator onto HC16 mutable schema | michaelstaib | **Out of scope.** Rewrites the same generators items 1 and 2 touch; item 1 relies on `descriptor.InterfaceType` - confirm it survives vnext. If vnext lands upstream, re-evaluate retiring the fork. Build against `main`, not vnext. |

> **Full backlog:** there are ~96 open `strawberry shake`-labelled issues upstream. The table
> above is the actionable candidate set (open PRs + the Razor/Blazor-relevant issues). Ask to
> append the full open backlog, or any filtered slice (e.g. all codegen bugs), if you want to
> triage beyond this set.

## Retiring the fork

Each item tracks an upstream PR/issue. When upstream merges an equivalent: drop the topic
branch, rebuild affected `release/fork-*` branches without it, and update this file. When all
items are upstream, delete the fork.
