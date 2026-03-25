# New Feature

**Purpose**: General-purpose template for implementing a new feature

## Context

This is a flexible template for any feature that doesn't fit other templates. Use this as a starting point and adjust based on the specific feature.

## Input

- **Feature name**: What are we building?
- **Purpose**: Why do we need this?
- **Scope**: What files/modules are involved?
- **Dependencies**: What other systems does it depend on?

## Analysis Phase

Before implementation, analyze:

1. **Architecture impact**:
   - Does this affect Client, Server, or both?
   - Is it Shared or game-specific?

2. **ECS integration**:
   - Does it need components/systems?
   - How does it interact with existing ECS data?

3. **Network impact**:
   - Does it need RPC?
   - What data needs to be synced?

4. **Performance**:
   - Will it run on Hot Path?
   - Any allocation concerns?

5. **Module dependencies**:
   - What modules does it depend on?
   - Is it independent enough?

## Output

**Implementation checklist:**
- [ ] Components (if needed)
- [ ] Systems (if needed)
- [ ] RPC (if network communication needed)
- [ ] Module (if new module required)
- [ ] Registration/initialization

## Constraints

- **DO** follow KarpikEngine architecture patterns from AGENTS.md
- **DO** consider Client/Server/Shared separation
- **DO** use ECS for data management
- **DO** avoid allocations on Hot Path
- **DO NOT** create tight coupling between modules