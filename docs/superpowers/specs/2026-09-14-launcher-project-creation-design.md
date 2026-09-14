# Launcher project creation design

## Status

Approved for implementation planning on 2026-09-14.

## Goal

Let the stable Karpik launcher create a game in a user-selected parent directory. The user chooses an installed Karpik SDK version, one of the templates shipped with that SDK, a project name, and whether to open the project immediately.

The result is a complete project at `<parent directory>/<project name>`. The launcher must never overwrite an existing directory.

## User flow

The launcher gains a `Create project...` action next to its existing project-open action. Its dialog contains:

- SDK version selector. It contains only validated installed engine payloads, sorted newest-first. The newest version is selected initially, but the user may choose any listed version.
- Template selector. It is populated only after selecting an SDK version.
- Project name.
- Parent directory picker.
- `Open after creation` checkbox.

The create command is disabled while the launcher is busy and until all required inputs form a valid request. A successful creation always adds the new solution to recent projects; editor startup is performed only when `Open after creation` is selected.

## SDK template contract

Each versioned engine payload continues to place SDK packages below `sdk/`. A payload that offers project creation additionally places:

- one or more ordinary `dotnet new` template `.nupkg` files; and
- `sdk/templates.json`, a catalog whose entries contain a stable template id, display name, description, short name, and the package file name.

The payload content hash already covers the `sdk/` directory, so this catalog and the template packages are integrity-checked as part of installation validation. The catalog is deliberately optional for backwards compatibility: an existing valid payload without it can still open its projects, but supplies no templates for new-project creation.

`templates.json` is the launcher UI contract. The launcher must validate it: required fields are present, identifiers and package paths are safe, paths stay beneath the selected payload's `sdk/` directory, package files exist, and template ids and short names are unique. Invalid catalogs expose an actionable diagnostic and no selectable templates.

Each template uses the normal .NET template engine. Templates that produce Karpik games declare a required `karpik-sdk-version` parameter and write it into the generated `global.json` as the `Karpik.Engine.Sdk` version. The launcher supplies the exact `MsBuildSdkVersion` from the selected installation. This keeps the generated game pinned to the matching editor and payload.

## Creation transaction

The selected parent directory must exist. The project name is a non-empty, safe single directory segment; `.` and `..`, separators, and invalid filename characters are rejected. The target directory is `<parent>/<name>` and must not exist.

The launcher resolves the selected SDK through `EngineInstallationResolver`, then uses only the catalog package from that validated installation. It creates an owned temporary `dotnet new` hive and a unique staging directory under the selected parent directory. It installs the selected package into that private hive and invokes:

```text
dotnet new <shortName> --name <name> --output <staging> --karpik-sdk-version <exact selected version>
```

The private hive means project creation neither sees nor changes the user's global template registrations. After generation, the launcher requires exactly one `.slnx` file at the generated project root and verifies its `global.json` pins the SDK version selected in the dialog. It publishes the result with `Directory.Move(staging, target)`, which is same-volume because staging is a sibling of the target.

On failure or cancellation, the launcher deletes only its owned staging directory and private hive. It never replaces an existing target. An abnormal process exit can leave owned temporary data for inspection, but never produces a visible partial project at the requested target.

## Components

`EngineInstallationResolver` gains the smallest public enumeration operation needed by the launcher: enumerate valid installed payloads and their manifests. It owns installation discovery and validation; the launcher does not duplicate `%LocalAppData%/Karpik/Engines` traversal.

`Karpik.Engine.Packager` packages template `.nupkg` outputs and the catalog into the existing `sdk/` payload directory. The project template package is versioned alongside the selected SDK.

`Karpik.Launcher` adds a focused project-creation service. It loads and validates the catalog, manages the private `dotnet new` invocation, validates generated output, and returns a structured result. `LauncherViewModel` owns dialog state, commands, busy state, status localization, recent-project registration, and optional reuse of the existing launch path. The Avalonia view supplies the dialog and folder picker.

No game, ECS, update loop, renderer, networking, client/server, or runtime bundle code changes. The work is limited to package construction and desktop startup tooling.

## Errors and diagnostics

Failures are expressed as actionable status messages for: no valid SDK payload, no catalog, malformed catalog, missing package, unsafe name, unavailable parent directory, existing target directory, template engine failure, missing or multiple generated solutions, and SDK mismatch in generated `global.json`.

The dialog remains usable after a failure. A cancellation reports cancellation without registering or opening a project.

## Verification

- Tooling tests enumerate only validated engine payloads and sort SDK versions correctly.
- Packager tests prove the template package and catalog are in the hashed `sdk/` directory.
- Launcher service tests cover valid creation, invalid name, existing target, malformed catalog, missing package, template failure cleanup, generated solution cardinality, and generated SDK mismatch.
- View-model tests cover default newest SDK selection, template refresh on SDK change, optional launch, recent-project registration, and busy-state behavior.
- Run the targeted launcher, tooling, and packager test projects with single-node MSBuild and inspect the generated project flow manually with one packaged payload.
