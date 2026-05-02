---
name: karpik-testing
description: KarpikEngine testing standards. Use when Codex adds or reviews unit tests, integration tests, edge-case tests, property-based tests, BenchmarkDotNet benchmarks, allocation checks, or module verification strategy.
---

# Karpik Testing

## Required Coverage
For each module or meaningful function, consider:

- Unit tests for isolated behavior.
- Integration tests for interactions between systems.
- Edge-case tests for boundaries and invalid inputs.
- Property-based tests for invariants over generated data.
- Benchmarks or allocation checks for hot paths.

## Edge Cases
Cover:

- normal values;
- zero and one;
- negative values;
- `int.MaxValue`, `int.MinValue`, `float.MaxValue` when relevant;
- null and empty collections;
- exact boundaries.

Example:

```csharp
[Theory]
[InlineData(0)]
[InlineData(1)]
[InlineData(-1)]
[InlineData(int.MaxValue)]
[InlineData(int.MinValue)]
public void Rectangle_Contains_EdgeCases(int value)
{
    var rect = new Rectangle(0, 0, 100, 100);
    var point = new Vector2(value, 50);

    var result = rect.Contains(point);

    Assert.Equal(value >= 0 && value <= 100, result);
}
```

## Property-Based Testing
- Generate 100-1000 random inputs for non-trivial pure logic.
- Check invariants, not only fixed expected values.
- Use FsCheck or the repo's existing property testing stack.
- Keep generated tests deterministic enough for CI diagnostics.

## Performance Tests
- Use BenchmarkDotNet for performance-sensitive code.
- Enable allocation measurements for hot paths.
- Treat unexpected allocations in frame-loop code as failures unless justified.

## Integration Tests
Test realistic subsystem flow, for example:

- input -> hit test -> widget events;
- network serialization -> deserialization -> handler;
- ECS setup -> system run -> component state.
