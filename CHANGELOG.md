# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2025-09-09
### Added
- `IPoolAdapter` interface and default `PoolAdapter` to decouple pooling from game code (used by Runtime Spawner).
- `EnsurePool(GameObject prefab, int capacity)` to lazily create/resize pools at runtime.
- `Prewarm(GameObject prefab, int count, Transform parent = null)` utility for predictable frame times.
- Per-pool organization under an optional parent transform (keeps Hierarchy tidy when many pools exist).
- Diagnostics: lightweight counters (`ActiveCount`, `InactiveCount`, `TotalCount`) and optional debug logging hooks.

### Changed
- `Get(prefab)` guarantees the instance is returned **inactive**; consumer code can activate after placement.
- Growth policy tuned to avoid large spikes (gradual expansion instead of big jumps).

### Fixed
- Safe, idempotent `Return(go)` (double-return guarded; ignores already-destroyed instances).
- Eliminated rare NREs when returning an object after its components were removed.
- Correctly clears internal maps on domain reloads / scene changes (no orphaned entries).

## [1.1.0] - 2025-04-13
- some fixes for how the pools are created & expanded
- add ability to create the object pool under a specific parent transform (specifically for UI pooling)
- add support for UI elements in object pooler
- add proper license, changelog, coc, contributing docs

## [1.0] - 2024-02-15
- initial version
