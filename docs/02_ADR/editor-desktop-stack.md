---
title: "Desktop stack for the 0.6 editor"
date: "2026-07-13"
status: "accepted"
tags:
  - adr
  - editor
  - tooling
  - client
---

# Desktop stack for the 0.6 editor

> Status: accepted
> Date: 2026-07-13
> Owners: KarpikEngine developers

## Context

Version 0.6 needs a cross-platform base editor with project and asset browsing, dockable panels, an ECS inspector, and game preview. ImGui.NET is already available for runtime diagnostics, but an immediate-mode UI is not suitable as the main shell for a full desktop UX: it would require recreating normal project navigation, complex property editing, drag-and-drop, and accessibility behaviour.

The engine already uses Veldrid and SDL2 for the client runtime. Editor tooling must not add allocations, blocking operations, or UI dependencies to `Update`, `FixedUpdate`, ECS systems, or Shared/Server assemblies.

## Decision

Create a separate side-neutral `Karpik.Editor` orchestration application using:

- Avalonia 12 Desktop with the Fluent theme as the desktop UI shell;
- Dock 12 (`Dock.Avalonia`, `Dock.Model.Mvvm`, `Dock.Serializer.SystemTextJson`) for dockable and floating panels and persisted layouts;
- ReactiveUI as the sole MVVM framework for editor view-models and asynchronous UI event composition;
- Avalonia `StorageProvider` for platform file/folder dialogs;
- existing Veldrid/SDL2 only for the game preview and runtime debug overlay.

For 0.6, the Veldrid preview is isolated from the Avalonia visual tree: it runs in a separate SDL2 window or process. The editor and runtime communicate through editor-facing bridge contracts and bounded snapshot/batch messages. The preview must not expose ECS pools, graphics types, or ReactiveUI types to the editor.

`Karpik.Editor` is not a Client or Server composition root. The selected versioned engine installation provides the side-specific runners and first-party engine modules, while the active external game build owns its physically separated Client and Server bundles. The desktop process may own one server session and multiple explicitly created client sessions. Stopping or losing the server stops all clients; clients cannot start without a running server.

The session selected in the «Сессии» panel is the sole source of editor snapshots. Hierarchy and Inspector are cleared immediately on selection changes, and a response from a previously selected process is discarded. Client snapshots execute as rare diagnostic work on the gameplay simulation thread; server snapshots execute at the server tick safe point. Snapshot polling remains bounded to one selected backend at 4 Hz.

ImGui.NET remains limited to runtime/debug overlays inside the Veldrid preview; it is not the editor shell.

## Alternatives Considered

### ImGui.NET editor shell

Rejected for the base editor. It is appropriate for debug overlays but does not provide the required desktop UX without rebuilding substantial UI infrastructure.

### Avalonia with embedded Veldrid surface

Deferred. Cross-platform native/GPU surface embedding has clipping, z-order, transform, and platform-specific resource-sharing constraints. It can be evaluated later as an isolated rendering-integration project after the editor is usable.

### Avalonia with CommunityToolkit.Mvvm

Rejected as the default. ReactiveUI better fits composed editor events such as debounced search, cancellable imports, diagnostics streams, and command state. The project must not mix both base MVVM frameworks.

## Consequences

- Windows, macOS, and Linux receive the same editor architecture.
- Docked panels, project navigation, property editing, drag-and-drop, and native file dialogs use a dedicated desktop UI stack.
- Runtime frame-time behaviour remains isolated from UI layout and reactive subscriptions.
- The editor owns subscription lifetimes: panels must dispose their ReactiveUI subscriptions when closed or deactivated.
- Preview integration is initially less seamless than an embedded viewport, but it avoids coupling two rendering lifecycles and makes a later attach-to-running-game workflow possible.
- Client and Server module graphs remain physically separated; the Avalonia process imports neither side's gameplay modules.
- Each client asks LiteNetLib to bind an available UDP port atomically, allowing multiple local client processes.

## Validation

- Run the editor on Windows, macOS, and Linux with project selection, layout persistence, drag-and-drop, and file dialogs.
- Verify closing/reopening docked panels does not retain subscriptions or view-models.
- Measure preview frame allocations and frame time with the editor open; editor diagnostics must not add allocations to runtime hot paths.
- Add integration coverage for editor bridge snapshot delivery and preview start/stop lifecycle.
- Start one server and at least two clients, verify distinct processes and two server-side connections, switch snapshot source between all sessions, then stop the server and verify cascade shutdown.
- Open a second external game through the real project coordinator while the first server and two clients are active; verify all old processes, reload artifacts, and module-file locks are released before the second project is published, and verify runners remain installation-owned while bundles remain game-owned.

## Recorded acceptance

On 2026-09-30, the complete Editor suite passed 142 tests with seven explicit capability skips, including real external-project replacement and server/two-client teardown. On 2026-10-01, Windows desktop smoke verified workspace persistence, UI start/stop/build and Debug/Release publish, session-selected Hierarchy/Inspector, and Release Launcher handoff between two distinct SDK installations. All observed old worker PIDs exited, both games' source Content remained unchanged, and original user settings were restored. This records Windows delivery evidence; cross-platform and runtime-allocation measurements above remain separate requirements.

See `plans/0.6-tail-acceptance-execplan.md` for detailed results and the remaining code-graph publication gate.

## Links

- Related roadmap: [[../04_Roadmap/karpikengine-1.0-roadmap]]
- Related roadmap: [[../04_Roadmap/kanban-0.6-2d-runtime]]
