---
name: karpik-engine-performance
description: Real-time C# performance rules for KarpikEngine. Use when Codex works on hot paths, frame loops, ECS systems, allocation-sensitive code, cache locality, SIMD, NativeMemory, Unsafe, lock-free structures, or low-level optimization.
---

# KarpikEngine Performance

## Real-Time Gate
Before accepting an architecture, check:

- whether it allocates managed memory per frame;
- whether it creates pointer chasing through class hierarchies, Dictionary, LINQ, closures, iterators, or boxing;
- whether data is dense in memory and traversed predictably;
- whether it introduces lock contention, blocking I/O, or waits in the game loop;
- whether random access can become a linear pass over arrays.

Reject solutions that create GC pressure or destroy cache locality in hot paths. Offer a no-GC, data-oriented alternative.

## Preferred Patterns
- Use `struct`, `readonly struct`, `ref`, `in`, `ref readonly`, `Span<T>`, and `ReadOnlySpan<T>`.
- For bulk processing, prefer SoA or dense arrays of structs over object graphs.
- For temporary buffers, use stackalloc, preallocated buffers, or pools.
- For long-lived unmanaged memory, consider `NativeMemory` with explicit ownership and release.
- For CPU-bound tight loops, consider `System.Numerics.Vector<T>` or hardware intrinsics (`Sse`, `Avx`) after profiling.
- Avoid LINQ, `foreach` over interfaces, async state machines, and delegates in hot paths.

## Three-Level Answer Pattern
When discussing architecture or optimization, present three levels:

1. High-Level: safe C# without premature low-level code.
2. High-Performance: no-GC, `struct`/`ref`, pools, dense arrays.
3. Hardcore: `Unsafe`, unmanaged memory, NativeAOT, SIMD/intrinsics.

For each level, evaluate maintainability, performance, and memory safety.

## Validation
- Add BenchmarkDotNet or measurable metrics for disputed optimizations.
- Check allocations through BenchmarkDotNet `MemoryDiagnoser`, a profiler, or allocation-budget tests.
- Optimize after identifying hot paths, but do not accept obviously GC-heavy architecture.
