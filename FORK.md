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
- **Fork PR:** [#3](https://github.com/stackworx-dotnet/graphql-platform/pull/3) (the head branch
  lives on the `stackworx` fork; assemble by cherry-picking commit `92e37555e7`).
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
- **Fork PR:** [#2](https://github.com/stackworx-dotnet/graphql-platform/pull/2).
- **Branch:** `feat/razor-persistent-state-attribute`.
- **Goal:** operation results survive the prerender -> interactive boundary so the query is not
  re-executed on the client after a server prerender (`InteractiveAuto` / `InteractiveServer`).
- **The showstopper (from #7209):** operation results are **interfaces** (`IGetBarsResult`), which
  `System.Text.Json` (and so `PersistAsJson` / `TryTakeFromJson`) cannot (de)serialize, and the
  concrete result classes reference entities in `IEntityStore`, so they do not round-trip either.
- **Approach (shipped):** use .NET 10's declarative `[PersistentState]` with a custom
  `PersistentComponentStateSerializer<IOperationResult<T>>`. The serializer persists the **raw
  transport `data` payload** (captured into `IOperationResult.ContextData`) and rehydrates it
  through the existing generated result builder (`BuildFromPersistedData` -> `IEntityStore` ->
  typed result), avoiding interface serialization entirely. The generated client auto-registers
  one serializer per result type (singletons, behind `#if NET10_0_OR_GREATER`); opt in with
  `"razorPersistedState": true` in `.graphqlrc.json`. Consumer usage is a plain component:
  `[PersistentState] public IOperationResult<IGetMeResult>? Result { get; set; }` then
  `Result ??= await Client.GetMe.ExecuteAsync(ct)`.
- **Constraints:** **.NET 10+ only** (the serializer type is .NET 10); snapshot, not reactive
  (no `Watch` / store-update subscription); requires a store and a `StrawberryShake.Razor` reference.
- **Earlier direction (closed):** a generated `UsePersistentQuery<T>` reactive component
  (fork PR [#1](https://github.com/stackworx-dotnet/graphql-platform/pull/1), branch
  `feat/razor-persistent-component-state`) was explored and closed as the wrong direction - it
  added a parallel component base plus an `OperationExecutor.Watch` overload for capability the
  serializer approach already covers. If reactive components ever need persisted state, enhance
  the existing `UseQuery<T>` on top of this serializer rather than reviving that base.
- **Touch points:** runtime `OperationResultBuilder` (capture + `BuildFromPersistedData`),
  `IOperationResultBuilder` (`BuildFromPersistedData` default-interface-method),
  `OperationResultPersistentStateSerializer` (`StrawberryShake.Razor`); generators
  `JsonResultBuilderGenerator` (capture override) and `DependencyInjectionGenerator` (serializer
  registration); the `RazorPersistedState` generator setting.
- **Build against current `main`, not vnext** (#9977), for fork longevity.

## Candidate issues & PRs

Triage table. Set **Address?** to `Yes` / `No` per row; anything marked `Yes` gets a topic
branch. The two tracked items above are pre-marked. PRs are the realistic cherry-pick units
(implementation already exists); issues without a PR are larger efforts.

| Address? | ID | Type | Title | Author | Branch / notes |
| --- | --- | --- | --- | --- | --- |
| **Yes** | [#9636](https://github.com/ChilliCream/graphql-platform/pull/9636) | PR | Use Operation interface type instead of runtime type (fixes #9635) | cliedeman | `fix/razor-operation-interface` - tracked item 1; fork PR [#3](https://github.com/stackworx-dotnet/graphql-platform/pull/3) |
| **Yes** | [#7209](https://github.com/ChilliCream/graphql-platform/issues/7209) | Issue | Make StrawberryShake play nicely with Blazor `PersistentComponentState` | nloum | `feat/razor-persistent-state-attribute` - tracked item 2; fork PR [#2](https://github.com/stackworx-dotnet/graphql-platform/pull/2) (supersedes closed [#1](https://github.com/stackworx-dotnet/graphql-platform/pull/1)) |
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

## Packaging & publishing

Which packages we actually fork, which we re-use from upstream, and how they are renamed.

### How Strawberry Shake ships (this decides what we fork)

The code generator is **not** a standalone package. There are two delivery surfaces:

- **Generator** (`StrawberryShake.CodeGeneration` + `StrawberryShake.CodeGeneration.CSharp`) is
  `IsPackable=false`. It is compiled into the `dotnet-graphql` CLI (`StrawberryShake.Tools`),
  which is then **bundled as binaries** (`tools/net8|9|10|11/dotnet-graphql.dll`) inside the
  build-integrated meta packages (`StrawberryShake.Blazor`, `.Server`, `.Maui`). At consumer
  build time `StrawberryShake.targets` runs that bundled DLL (`-r` turns on the Razor
  generators); it does **not** use the globally-installed tool. So a generator change reaches
  consumers through the **meta package that bundles it**, not through a generator package.
- **Runtime** ships as ordinary libraries (`StrawberryShake.Core`, `StrawberryShake.Razor`,
  `StrawberryShake.Transport.*`, ...). The Blazor base classes (`UseQuery` / `UseSubscription`
  / `DataComponent`) live in `StrawberryShake.Razor`.

Published-package dependency graph:

```
StrawberryShake.Blazor (meta) ── bundles ──> dotnet-graphql (StrawberryShake.Tools, carries the generator)
   └─ deps: StrawberryShake.Core, StrawberryShake.Razor, Transport.Http, Transport.WebSockets
StrawberryShake.Razor ── dep ──> StrawberryShake.Core
StrawberryShake.Server / .Maui (meta) ── bundle the same tool; deps: Core, Transport.Http, Transport.WebSockets (no Razor)
StrawberryShake (meta) ── dep ──> StrawberryShake.Core   (abstractions only: no tool, no Razor)
```

### What item 2 changes

- Generators `RazorQueryGenerator.cs` / `RazorSubscriptionGenerator.cs` (in `CodeGeneration.CSharp`)
  -> flow into the bundled `dotnet-graphql`.
- Runtime `UseQuery.cs` / `UseSubscription.cs` / `DataComponent.cs` (in `StrawberryShake.Razor`).

Both are **Blazor-only**: the runtime change is in `Razor`, and the generator change only fires
when `GraphQLRazorComponents=enable` (Blazor consumers). Server/Maui consumers neither reference
`Razor` nor generate Razor components, so the patched tool is inert for them.

### Minimal fork + publish set (item 2)

| Package | Why it must be forked | Publish? |
| --- | --- | --- |
| `StrawberryShake.Razor` | Contains the changed runtime base classes. | **Yes.** |
| `StrawberryShake.Blazor` | Bundles the patched `dotnet-graphql` (generator change) **and** depends on the changed `Razor`. This is what a Blazor consumer installs. | **Yes.** |
| `StrawberryShake.Tools` (global CLI) | Carries the patched generator. | **Optional** — only if you also publish the standalone `dotnet graphql` CLI for manual generation. The meta-package build path does not use it. |

### Re-use from upstream (do NOT fork)

Everything the change does not touch is consumed straight from ChilliCream's nuget.org packages at
the upstream base version: `StrawberryShake.Core`, `StrawberryShake.Transport.Http`,
`.Transport.WebSockets`, `.Transport.InMemory`, `.Persistence.SQLite`, `.Resources`,
`.Tools.Configuration`, the base `StrawberryShake` meta, `StrawberryShake.Server`,
`StrawberryShake.Maui`, and all `HotChocolate.*`. Server/Maui are **not** forked even though they
bundle the tool, because the generator change is inert without Razor generation.

### Rename scheme

NuGet reserves the `StrawberryShake` ID prefix to ChilliCream, so a public fork cannot republish
under any `StrawberryShake.*` ID. Forked packages are renamed under our `Stackworx.` prefix;
**only the `PackageId` changes** — `AssemblyName`, `RootNamespace`, and the public namespaces stay
`StrawberryShake.*`, so the fork is a drop-in replacement (existing `using StrawberryShake.Razor;`
and the generated code keep working).

| Upstream PackageId | Fork PackageId | AssemblyName (unchanged) |
| --- | --- | --- |
| `StrawberryShake.Razor` | `Stackworx.StrawberryShake.Razor` | `StrawberryShake.Razor` |
| `StrawberryShake.Blazor` | `Stackworx.StrawberryShake.Blazor` | `StrawberryShake.Blazor` |
| `StrawberryShake.Tools` (if published) | `Stackworx.StrawberryShake.Tools` | `dotnet-graphql` |

### Versioning & dependency pinning (the gotcha)

`dotnet pack` turns each in-repo `ProjectReference` into a package dependency at the **build
version** (`-p:Version=`). So how the forked packages reference the unchanged ones depends on the
version you publish at:

- **Model A — fork version == upstream base version (e.g. publish at `16.3.0`).** Leave the csproj
  `ProjectReference`s as-is. `Stackworx.StrawberryShake.Blazor 16.3.0` then emits deps on
  `StrawberryShake.Core 16.3.0` / `Transport.* 16.3.0` (resolve to the upstream originals) plus
  `Stackworx.StrawberryShake.Razor 16.3.0` (our fork). Simplest, and the rename below supports it
  directly. Limitation: you cannot ship a second fork build of the same base (can't push `16.3.0`
  twice) — this conflicts with the `release/fork-16.0.0.<ext>` iteration scheme above.
- **Model B — independent fork version (e.g. `16.3.0-stackworx.1`).** Required for iterating on a
  base. Pin the **unchanged** deps explicitly: in the forked csproj replace the unchanged
  `ProjectReference`s with `PackageReference`s pinned to the upstream base (e.g.
  `StrawberryShake.Core` `16.3.0`), and keep the `ProjectReference` to the other **forked** project
  (`Razor`). Otherwise they would resolve to a non-existent `StrawberryShake.Core 16.3.0-stackworx.1`.
  Do this in a dedicated pack step / on the `release/fork-*` branch, **not** in the shared dev
  csproj — pointing the meta package at a NuGet `Core` while its siblings build from source can
  confuse the local `All.slnx` build.

Recommended: publish at the upstream base version (Model A) for the first release; switch to pinned
`PackageReference`s (Model B) only when you need to iterate on a base.

## Retiring the fork

Each item tracks an upstream PR/issue. When upstream merges an equivalent: drop the topic
branch, rebuild affected `release/fork-*` branches without it, and update this file. When all
items are upstream, delete the fork.
