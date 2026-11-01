# Reuse individual content processor results between builds

This ExecPlan is a living document maintained according to `plans/PLANS.md`. It starts with a proposed design; product-code implementation is not authorized by this document alone. Repository instructions require the developer to approve the implementation direction and explicitly authorize multi-file code changes.

## Purpose / Big Picture

The first Content slice toward 0.7 makes an ordinary CLI or SDK content build reuse successful processor results for unchanged assets even when another asset changes. Given 500 independent textures and one edited texture, the next build invokes the texture processor once, preserves the other results, and publishes the same manifest and artifact bytes as a clean build from the same inputs.

Editor work stays last in 0.7 as requested. This slice does not add runtime hot reload, a watcher, background builds, build-time consumption of other artifacts, atlases, a global cache, or new CLI switches. It prepares those later features without depending on them. Full source scanning and byte hashing remain intentional: the first optimization removes unnecessary processing rather than promising constant-time filesystem discovery.

## Progress

- [x] (2026-11-01) Inspected the coordinator, processor interface and four built-in processors, hashing, manifest, atomic publisher, content tests, and accepted build/target/source-format ADRs at baseline `7224b18`.
- [x] (2026-11-01) Created task branch `codex/content-incremental-build` from clean `main`.
- [x] (2026-11-01) Prepared the proposed design in this document.
- [x] (2026-11-01) Baseline Content suite: 111 passed, zero failed/skipped. Initial sandbox run compiled but VSTest could not open its local socket; approved execution outside the sandbox passed using the same binaries.
- [x] (2026-11-01) Independent builder check identified a possible no-op I/O regression; resolved by retaining a verified early return before staging when every selected asset is reused and the candidate manifest is unchanged. No other material questions were reported.
- [ ] Developer approves the design and explicitly chooses whether to authorize implementation or implement from this plan themselves.
- [ ] Expand the approved design into implementation steps using the writing-plans workflow, retaining this ExecPlan as the authoritative task document.
- [ ] Implement and validate per-asset reuse.
- [ ] Implement and validate cache recovery and complete publication.
- [ ] Complete clean/incremental equivalence and CLI regression checks; update relevant documentation and publish one focused PR after implementation is authorized and verified.

## Surprises & Discoveries

- Observation: there is already a whole-tree no-op optimization. A changed tree still processes every selected asset, while an unchanged tree skips processors after scanning and reading source bytes.
  Evidence: `ContentBuildCoordinator.Build`, `TryComputeInputFingerprint`, and `TryLoadReusableOutput` in `src/Karpik.Content.Core/ContentBuildCoordinator.cs`; `Build_UnchangedInputs_DoesNotRunProcessorAgain` in `tests/Karpik.Content.Tests/ContentBuildCoordinatorTests.cs`.
- Observation: the published artifact locator is a recipe digest, not a hash of cooked bytes. Existing reuse checks only existence and length.
  Evidence: `src/Karpik.Content.Core/ContentHashing.cs` and `ContentBuildCoordinator.TryLoadReusableOutput`. Same-length corruption needs a separate cooked-byte checksum to detect it without processing again.
- Observation: staging must contain all artifacts referenced by the new manifest, including reused artifacts. Omitting a reused file makes the current publisher classify it as stale.
  Evidence: `backupArtifactMap` cleanup in `src/Karpik.Content.Core/ContentAtomicPublisher.cs`.
- Observation: processors receive the relative source path and may emit dependencies and diagnostics that are not all represented by their source metadata.
  Evidence: `src/Karpik.Content.Core/IContentProcessor.cs`; texture and shader extension validation uses `relativePath`. The current scanner validates metadata dependencies before processing, and `Build` later merges processor dependencies into the manifest.

## Decision Log

- Decision: scope the first PR to per-asset build reuse; dependency-content invalidation and runtime hot reload remain subsequent tasks.
  Rationale: existing processor arguments contain only this source, its metadata, relative path and target. Referencing another `AssetId` does not imply that its cooked bytes are embedded in this result. Introducing a dependency resolver or recooking all transitive references would enlarge the contract without a current consumer.
  Date/Author: 2026-11-01 / proposed by Codex, pending developer approval.
- Decision: use a versioned private output-local build index; keep the public manifest schema, artifact recipe format, CLI invocation and processor interface unchanged.
  Rationale: the manifest alone omits processor diagnostics, path-sensitive reuse inputs and cooked-byte integrity information. A disposable index can preserve these without shipping a new runtime schema.
  Date/Author: 2026-11-01 / proposed by Codex, pending developer approval.
- Decision: use safe managed C# in the headless build tool, retain synchronous single-owner publication, and defer scan optimizations and parallel cooking until measured.
  Rationale: this work is outside runtime hot paths. Avoiding redundant decoding is the expected gain; Unsafe/SIMD and a scheduler add complexity without evidence of a need.
  Date/Author: 2026-11-01 / proposed by Codex, pending developer approval.
- Decision: preserve a verified all-hit fast path without creating staging or invoking the publisher.
  Rationale: copying and comparing all cooked files on an unchanged build would regress existing no-op behavior. This return is allowed only after current input validation, cache-key/integrity verification, diagnostic replay, effective-graph validation and candidate-manifest equality checks.
  Date/Author: 2026-11-01 / proposed by Codex following independent builder review, pending developer approval.

## Outcomes & Retrospective

Design draft only. No product code, test code, manifest schema, or runtime API has been changed. Implementation outcomes and measured processor-call reductions must be recorded after approval and execution, not inferred from this proposal.

Baseline verification on .NET SDK 10.0.114: `dotnet test tests/Karpik.Content.Tests/Karpik.Content.Tests.csproj -m:1 -nr:false --no-restore --no-build` passed 111/111. Three existing xUnit2029 warnings were emitted during the initial compilation; the sandbox prevented testhost communication, so only execution was repeated outside it. This establishes the pre-change baseline, not validation of a new implementation.

## Context and Orientation

`src/Karpik.Content.Core/ContentBuildCoordinator.cs` scans and validates source/sidecar pairs, selects assets for a target, invokes processors, writes a staging tree and calls `ContentAtomicPublisher`. `src/Karpik.Content.Tool/Program.cs` hosts the CLI. SDK consumers use the same tool; the new behavior should be automatic through the current `build` command.

An `AssetId` is the durable GUID stored in a sidecar, whereas a locator identifies a cooked file in one output. A recipe identifies processing inputs; a cooked checksum identifies resulting bytes. These serve different purposes. A cache hit means processing can be skipped; it does not mean copying files into staging or reading inputs is eliminated.

Processors implement `src/Karpik.Content.Core/IContentProcessor.cs`. The default processors are RawJson, Texture, FontJson and Shader. Their deterministic contract is documented in `docs/02_ADR/content-pipeline-build-contract.md`. Changing processor behavior requires changing its version; no cache can detect an unversioned behavior change reliably.

Accepted constraints also come from `docs/02_ADR/content-target-profiles.md` and `docs/02_ADR/content-pipeline-texture-source-format.md`. Runtime code in `src/Karpik.Content.Runtime/` is outside this slice. Existing source/target validation must run on every build, including a potential no-op.

## Approaches

1. Match only existing manifest locators against current recipe hashes. This is the smallest edit, but cannot faithfully replay processor warnings or validate cooked-byte integrity and lacks relative-path input history. Rejected as an incomplete reuse contract.
2. Store an output-local private index tied to the published manifest. This adds one small internal schema and checksum I/O but preserves complete processor results and permits conservative cache misses. Recommended; approval pending.
3. Add a global content-addressed cache, filesystem watcher and dependency-aware scheduler together. This could serve multiple projects but adds eviction, concurrent writers and more lifetime rules. Deferred because one-output reuse is independently useful.

## Proposed Design

### Reuse identity and index contents

Add an internal helper, tentatively `src/Karpik.Content.Core/ContentBuildCache.cs`, with a versioned index at `.karpik-content-cache.v1.json` under the output root. The filename and helper organization are implementation choices, not public API.

The index contains its schema version, build namespace, selected target, a digest of the canonical published manifest, and records sorted by `AssetId`. Each record stores the asset ID, a framed reuse key, the corresponding manifest entry, the cooked-byte SHA-256, and successful processor diagnostics. The manifest entry already includes the sorted union of metadata and processor dependencies. Only successful results may be persisted; diagnostics ordering remains deterministic.

The framed reuse key includes normalized relative path, source-byte digest, full canonical metadata, processor declared type/version, selected target and a cache algorithm version. Including the relative path conservatively reruns processing on moves and extension changes even if the stable ID survives. Absolute source/output paths and timestamps are excluded. The public `ComputeArtifactHash` algorithm is unchanged; the private reuse key is allowed to be more conservative than the artifact locator.

A cache is usable only when its schema, namespace and target match, its manifest digest matches the published manifest, its records are unique and internally consistent with that manifest, and referenced artifact paths are normalized and contained under the expected artifact directory. Never accept a path directly from the index as an arbitrary filesystem location. Unknown schema, duplicate records, invalid locators, invalid JSON and expected cache-read I/O failures fall back to processing rather than trusting partial metadata.

### Build flow

Run existing source and metadata validation first. Load the published manifest and matching index. For each selected source in stable ID order, compute its reuse key. A matching record is a hit only if its artifact exists and both its size and cooked checksum match. Missing or damaged artifacts cause a miss for that asset. A miss calls the processor and collects the same manifest entry and diagnostics as a clean build.

Validate the effective dependency graph for the complete candidate result, including dependencies emitted by processors. Unknown IDs, target-incompatible dependencies and cycles must fail both a clean build and an incremental build consistently. Extract or share the current metadata-graph validation where needed rather than implementing different clean/cache policies. Metadata-only prevalidation remains useful before expensive processing.

Treat current dependency IDs as references: a changed referenced asset does not by itself invalidate an unchanged processor result. Removing a referenced asset still fails effective-graph validation. Supporting processors that actually read another asset's content needs a later explicit build-dependency contract and recipe changes; this slice must not claim that capability.

If every selected asset was a verified cache hit and the canonical candidate manifest is byte-identical to the current manifest, return success with the current manifest and complete diagnostics before creating staging or invoking the publisher. This preserves the no-op path without writing/copying the entire cooked tree. Deletions or selection changes that alter the manifest do not qualify, even if every remaining asset is a hit. Checksum reads remain an explicit added integrity cost.

Otherwise materialize a complete staging tree. Copy verified reused artifact files into it with ordinary safe filesystem APIs; do not use hardlinks or a new platform-specific storage strategy. Fresh results are written normally. Verify reused copies against their expected checksum before publication so validation is not separated from staging by an unchecked read. Write and validate the canonical manifest, then use the existing publisher. Its no-rewrite comparison should preserve the published files for identical artifacts.

Publish the cache index only after successful manifest/artifact publication, by writing an output-local temporary file and atomically renaming/replacing it. It is advisory rather than part of runtime output correctness. Failure to save the index does not turn a successfully published content build into a failed build. A stale index left by a failed build or crash cannot be accepted against a different manifest digest. If old and new canonical manifests happen to match, exact reuse-key and cooked-checksum checks still guard reuse.

The current whole-tree fingerprint must no longer bypass these integrity and diagnostic guarantees. Retire its early return or route it through verified index hits; do not maintain two independent policies. Old output without the new index is supported by a successful cold rebuild. `Validate` remains a full validation operation and must not silently become cache-only validation.

### Publication and ownership boundaries

Keep one writer per output root as the supported model. This task does not add concurrent CLI writers or promise snapshot-isolated input reads during an edit. Every candidate result comes from the source bytes captured by its current scan. Failed scanning, processing, dependency validation or staging must not call the publisher or overwrite the previously successful index.

The existing publisher has journal-based recovery and manifest-last replacement, but this investigation is not proof of crash-safe multi-file snapshots for arbitrary I/O failures. Retain and test its existing guarantees; record any discovered publisher correctness blocker before expanding scope. Do not delete artifacts earlier or omit reused files from staging. Runtime use of old generations and GPU-resource retirement are later hot-reload concerns.

## Real-Time Assessment

This modifies headless build-time tooling only. Scanning, hashing, copying, JSON serialization and managed collections may allocate; none may move into Update, FixedUpdate, ECS Run, render submission or network processing. It adds no locks or waits to runtime and no Client graphics/input dependency to Core or Server. Physics and fixed-dt behavior are unaffected. Dense runtime caches, ECS components, native memory and SIMD are unnecessary here. The expected improvement is fewer processor invocations; disk scans and copies remain explicit limitations.

## Plan of Work

Detailed implementation sequencing is pending approval. Expected change points are `ContentBuildCoordinator.Build` and its whole-tree reuse helpers; a private index helper in Core; extraction of effective dependency validation if required; tests in `tests/Karpik.Content.Tests/ContentBuildCoordinatorTests.cs` or a focused new test file; and CLI integration tests in `tests/Karpik.Content.Tests/CliTests.cs`. Keep `ContentAtomicPublisher` unchanged unless acceptance reveals a necessary correctness change. Do not modify Editor or Runtime.

After approval, document the advisory cache and reference-dependency semantics in the existing build-contract ADR. Do not mark the roadmap's broader dependency invalidation or content hot reload items complete for this bounded slice.

## Milestones

### 1. Baseline and approved design

Run the existing Content suite, complete the builder check, record results and resolve substantive design questions. Obtain developer approval of this document and implementation authorization before writing product code. Expand concrete implementation steps only after that approval.

### 2. Per-asset reuse and complete candidate validation

Prove processor counts for initial, unchanged and one-asset-changed builds, including a new coordinator instance to prove persistent reuse. Preserve successful diagnostics and validate the full effective dependency graph for reused and processed records. End with focused tests and record results before proceeding.

### 3. Recovery, complete staging and publication

Prove corrupt/stale cache fallback, same-size artifact corruption detection, preservation of reused artifacts through publication, and last-good output after prepublication failures. End with targeted recovery and coordinator tests.

### 4. Consumer equivalence and review

Prove clean/incremental canonical output equivalence across edits, additions, deletions and moves; run the full Content suite and a CLI build regression. Build the smallest affected host, review the final diff and document compatibility. Publish this task branch as one PR only after implementation is authorized and verification is complete.

## Concrete Steps

All commands run from `/home/artem/git/Karpik.Engine`. The baseline command is:

```sh
dotnet test tests/Karpik.Content.Tests/Karpik.Content.Tests.csproj -m:1 -nr:false --no-restore
```

Use this coordinator-focused command after the corresponding implementation milestone:

```sh
dotnet test tests/Karpik.Content.Tests/Karpik.Content.Tests.csproj -m:1 -nr:false --no-restore --filter FullyQualifiedName~ContentBuildCoordinatorTests
```

Run the full Content suite after final integration, then build the host:

```sh
dotnet build src/Karpik.Content.Tool/Karpik.Content.Tool.csproj -m:1 -nr:false --no-restore
git diff --check
```

If restore is required, record the reason and use the same targeted project rather than building the entire solution. New focused test classes require updating the milestone filter to include them. Successful commands must report zero failures/errors; do not infer a passing baseline from historical plans.

## Validation and Acceptance

- An initial build processes every selected asset; unchanged repeated builds process none, including after coordinator reconstruction. A verified all-hit unchanged build does not create staging, invoke publication or rewrite output/index files.
- Editing one independent asset processes only that asset. Import settings, other semantic metadata and processor version changes invalidate the appropriate entries.
- Client and Server cannot reuse each other's results. A source move retains `AssetId` but conservatively reruns processing, including extension-sensitive validation.
- A changed referenced asset does not spuriously recook its unchanged referrer. Removed/unknown/target-invalid references and cycles, including processor-emitted ones, fail before publication.
- Cache-hit processor warnings and diagnostic ordering match a clean build. Failed results are never persisted.
- Missing index, malformed/unsupported index, manifest mismatch, duplicate records and invalid locators produce a safe cold build. Missing or modified cooked files, including same-size edits, trigger processing instead of a false hit.
- Every reused artifact survives a changed build with its bytes intact; unchanged published files are not rewritten. Complete staging is proven with at least two assets where only one changes.
- Processor or validation failure preserves the last successful manifest, referenced artifact bytes and index. A stale/missing index after interruption is disposable; a cache-write failure after successful publication leaves usable output.
- Empty selected content and additions/deletions work. Clean and incremental outputs match byte-for-byte for the same final source tree. Include deterministic generated edit sequences if they add coverage beyond the fixed cases, using existing test infrastructure without a new dependency.
- CLI and existing Content tests remain green; runtime manifest schema, artifact hashing and ordinary build command remain compatible.

## Idempotence and Recovery

The new index is disposable. Removing only `.karpik-content-cache.v1.json` forces a cold build without changing source sidecars or stable IDs. Never remove a user's whole output/source tree as a troubleshooting step. Builds must be rerunnable after a failed processor or invalid metadata once inputs are fixed. A failed index save should cost subsequent processing time, not invalidate a successful manifest.

Do not add permanent test assets to user projects. Tests use owned temporary directories and validate paths. Keep existing publisher recovery tests, and do not represent manifest-last publication as a new live-resource lifetime guarantee. Reverting implementation requires no migration of source metadata or public runtime manifests; older tools can ignore the private index.

## Artifacts and Notes

Authoritative design and future execution record: this document. Baseline commit: `7224b18`. Task branch: `codex/content-incremental-build`.

Follow-up scopes, each with its own design and PR: explicit build-dependency consumption/invalidation for a real processor; successful-build change notifications and runtime resource replacement; watcher/debounce orchestration if needed. Editor remains deferred.
