---
name: karpik-networking
description: KarpikEngine networking rules. Use when Codex works on RPC, Network.Codegen, IReader/IWriter serialization, client-server validation, IPeer, ITargetRpcSender, delivery methods, connection state, replication, prediction, or network hot paths.
---

# Karpik Networking

## Client / Server Authority
- Server owns validation and authoritative game state.
- Client owns input, rendering and local prediction/presentation.
- Shared contains protocols, serializable payloads and shared deterministic logic.
- Do not import Server projects into Client or Client projects into Server.

## RPC
Key types:

- `IRpc` - base RPC interface.
- `ITargetRpcSender` - sends RPC to a specific peer.
- `IPeer` - connection/peer abstraction.

Rules:

- RPC methods use attributes such as `ServerRpc` and `ClientRpc`.
- Code generation goes through `Network.Codegen`.
- Do not send classes over RPC; use serializable structs.
- Do not call RPC every `Update` without throttle, batching or a queue.
- Prefer unreliable delivery for frequent state/input streams when loss is acceptable.
- Prefer reliable delivery for commands that must arrive exactly as protocol requires.

## Serialization
- Serialize through `IWriter` / `IReader`.
- Use existing `writer.Write(value)` / reader extensions.
- Keep payloads compact and explicit.
- Avoid allocations during serialization and deserialization.
- Be careful with `string`, arrays and variable-length payloads in hot paths.

## Connection Management
- Use `INetworkManager` for network lifecycle.
- Use `ConnectionState` for Disconnected / Connecting / Connected state.
- Use `DeliveryMethod` explicitly; do not rely on defaults in protocol-sensitive code.

## Performance Checks
- Batch small messages when it reduces packet overhead without adding unacceptable latency.
- Avoid per-peer allocations in broadcast loops.
- Avoid reflection in runtime networking paths; codegen should do the repetitive work.
- Keep protocol structs versionable and test serialization round-trips.
