---
name: karpik-hot-reload
description: KarpikEngine Hot Reload rules. Use when Codex works on HotReloadHandler, IPC reload flow, IpcClient/IpcServer, PluginLoadContext, plugin assembly loading, reload-safe state, ECS state preservation, or module reload behavior.
---

# Karpik Hot Reload

## State Model
- Gameplay state should live in ECS worlds so it can survive reload.
- Do not keep gameplay state in static fields.
- Do not keep references to objects outside ECS pools when that state must survive reload.
- Prefer component tags and ECS queries over `Dictionary<string, Entity>` style registries.

## Reload Flow
Key mechanisms:

- `HotReloadHandler` handles reload orchestration.
- `IpcClient` / `IpcServer` coordinate cross-process communication.
- `PluginLoadContext` loads plugin assemblies.

Rules:

- Constructors must stay light; heavy work slows reload.
- Module initialization must be explicit and repeatable.
- Dispose old resources deterministically before loading replacements.
- Be careful with event subscriptions; unsubscribe or bind through reload-aware lifecycle hooks.

## Safety Checks
- Check that resources owned by old assemblies can be released.
- Avoid static caches of types, delegates and reflection metadata from unloadable contexts.
- Keep reload boundaries explicit: persistent ECS data, reloadable code, disposable platform resources.
- Test repeated reloads, not only the first reload.
