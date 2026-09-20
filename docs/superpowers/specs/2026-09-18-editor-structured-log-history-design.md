# Editor structured log history

## Goal

The Editor console must show only runtime entries emitted through `ILogger.Log`,
keep every received entry independently of UI filters, filter the visible list by
minimum level and source session, and persist the entries on disk.

## Runtime output contract

The logger module keeps `AddSimpleConsole()` for normal human-readable output.
When a worker is launched by the Editor, an environment flag enables a second
provider registered by `AddEditorConsole()`.

`AddEditorConsole()` writes one physical stdout line for each `ILogger` entry:

```text
@karpik-editor-log:{"timestamp":"2026-09-18T12:34:56.789Z","level":"Information","message":"Player connected"}
```

The JSON serializer escapes newlines and control characters in `message`; one
event therefore remains one process-output line. The provider emits no category,
scope, or exception text because the editor contract is `level + message`.

The normal `SimpleConsole` output remains present in editor-launched workers but
is ignored by the Editor. `Console.WriteLine`, stderr, MSBuild output, and worker
lifecycle messages are also ignored by the log panel.

## Editor data flow

1. `ProcessManager` sets the environment flag only for editor preview workers.
2. `EditorPreviewController` forwards worker output unchanged.
3. `EditorShellViewModel` accepts only the marker and valid JSON records, adds
   the source session name, and hands the entry to the console model.
4. The console model retains a bounded in-memory list of all accepted entries;
   its level and session filters rebuild only the visible list.
5. The selected session list retains stopped sessions so their history stays
   filterable.

The existing 2,000-entry in-memory limit remains an editor responsiveness bound.
It does not limit the disk archive.

## Persistence

Each Editor launch owns one JSONL file under
`%LocalAppData%\KarpikEngine\Editor\logs`. Every persisted line contains the
runtime timestamp, source session, level, and message.

A single background writer owns the open file. Its bounded queue applies
backpressure to process-output handling rather than dropping records; it never
blocks the Avalonia UI thread. On a clean editor shutdown the writer flushes and
closes the file. Startup and shutdown prune oldest completed files until the
directory is at most 1 GiB.

The panel displays the active Editor launch only. Older JSONL files remain
inspectable on disk; browsing archives in the panel is out of scope.

## UI

The console tool contains:

- a minimum-level selector, defaulting to `Info` so routine diagnostics stay
  hidden until requested;
- a session selector, defaulting to `All sessions`;
- the existing Clear action, which clears only the active in-memory history and
  does not delete the archive.

Visible rows include session, level, and message on one row. `Trace` is
available as an opt-in display filter, while the logger emits trace entries so
they can be inspected when requested.

## Failure behavior and tests

Malformed marker JSON and all unmarked process output are ignored. If archive
creation or writing fails, the in-memory panel continues and the Editor reports
one non-recursive status diagnostic instead of feeding the failure back into the
log stream.

Tests will cover the protocol parser, single-line message serialization, level
and session filtering without history loss, bounded in-memory behavior, archive
JSONL content and rotation, and the editor-worker environment flag.
