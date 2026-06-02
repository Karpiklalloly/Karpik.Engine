---
name: maintaining-changelogs
description: Use when creating, updating, reviewing, or releasing a CHANGELOG.md file, summarizing user-visible changes, or choosing the next semantic version for a release.
---

# Maintaining Changelogs

## Overview

Maintain a human-readable `CHANGELOG.md` using [Keep a Changelog 1.1.0](https://keepachangelog.com/ru/1.1.0/) and choose releases using [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html). Write release notes for users and maintainers, not as a raw commit dump.

## Workflow

1. Read the existing `CHANGELOG.md` and repository version/tag conventions.
2. Collect user-visible changes from the requested scope. Exclude internal churn unless it affects behavior, compatibility, operations, or migration.
3. Add pending entries under `## [Unreleased]`. Group entries by category.
4. For a release, determine the next version from the most significant change, move relevant entries from `Unreleased` into `## [X.Y.Z] - YYYY-MM-DD`, and keep newest releases first.
5. Preserve historical formatting unless normalization is explicitly requested. Apply the standard format to new entries.
6. Verify headings, categories, links, date, and version decision before finishing.

## Changelog Format

Use only categories that contain entries:

- `Added` for new features.
- `Changed` for changes to existing behavior.
- `Deprecated` for features planned for removal.
- `Removed` for removed features.
- `Fixed` for bug fixes.
- `Security` for vulnerability fixes.

Prefer concise bullets describing impact. Add migration instructions when users must change code, configuration, data, or deployment steps.

```markdown
# Changelog

## [Unreleased]

### Added
- Added deterministic module dependency validation.

## [0.5.0] - 2026-06-02

### Changed
- Replaced direct module references with generated dependency metadata.
```

## Semantic Versioning

Use `MAJOR.MINOR.PATCH`:

| Increment | Choose when |
| --- | --- |
| `MAJOR` | A public API or supported behavior changes incompatibly. |
| `MINOR` | Backward-compatible functionality is added, including a backward-compatible deprecation. |
| `PATCH` | Backward-compatible defects are fixed. |

Apply the highest required increment when a release contains multiple changes. Reset lower components after an increment. Treat `0.y.z` as initial development: compatibility is not stable, but still explain breaking changes clearly.

Use pre-release suffixes such as `1.0.0-alpha.1` for unstable release candidates. Build metadata such as `1.0.0+build.17` does not affect precedence. Do not modify the contents of an already released version; publish a new version.

## Links

When the repository URL and tags are known, make versions linkable and add compare links at the bottom:

```markdown
[Unreleased]: https://github.com/org/repo/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/org/repo/compare/v0.4.0...v0.5.0
```

Respect an existing repository convention for a `v` prefix in Git tags. Keep the semantic version itself in `X.Y.Z` form.

## Review Checklist

- Keep an `Unreleased` section for pending work.
- List every released version with an ISO date: `YYYY-MM-DD`.
- Order releases newest first.
- Group changes by impact category, not by commit.
- Call out breaking changes and migration steps explicitly.
- Avoid ambiguous bullets such as `misc fixes`, implementation-only details, and duplicate entries.
- Confirm that the chosen version matches the largest compatibility impact.
