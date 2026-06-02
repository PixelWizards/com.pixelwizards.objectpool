# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2025-12-19

### Added

- **Prefab ID–based pool identity**
  - Pools are now keyed internally by **prefab instance ID**, not just prefab name.
  - Prevents collisions when multiple prefabs share the same name (critical for networking and addressable workflows).
  - Improves reliability when used with **Photon Fusion / PUN** and other networked spawning systems.
- **Time-sliced pool expansion**
  - Runtime pool expansion can now be processed **over multiple frames** to avoid frame spikes.
  - Expansion work is queued and budgeted per frame.
  - New global control:
    - `PoolManager.MaxInstantiatesPerFrame` — limits how many instances may be instantiated per frame during expansion.
- **Centralized expansion driver**
  - Pool expansion is now driven by a single active `PoolManager` instance in the scene.
  - Includes a safe fallback to immediate expansion if no manager instance is present.
- **Instance-to-pool tracking**
  - Instances now maintain an internal mapping back to their originating pool.
  - Makes `ReturnInstance` more robust and avoids reliance on name parsing in normal cases.

### Changed

- **Pool lookup priority**
  - `GetInstance(GameObject prefab)` now resolves pools by **prefab identity first**, falling back to name-based creation only when needed.
  - Eliminates edge cases caused by prefab renames or duplicate prefab names.
- **Runtime expansion behavior**
  - Runtime expansions now default to **time-sliced growth** instead of immediate bulk instantiation.
  - Initial pool creation and prewarm remain synchronous for predictability.
- **Growth strategy**
  - Pool growth now scales multiplicatively (minimum doubling) instead of fixed increments.
  - Reduces frequent small expansions under sustained load.
- **Safer pool creation**
  - The pool manager now checks for an existing pool before creating a new one, preventing accidental duplicate pools for the same prefab.

### Fixed

- **Networking-related spawn instability**
  - Fixes issues caused by relying on prefab names for pool identity, which could lead to incorrect pool reuse and mismatched instances in networked games.
  - Improves compatibility with **Fusion prefab tables**, pooled network objects, and runtime-instantiated prefabs.
- **Frame spikes during heavy spawning**
  - Eliminates large single-frame instantiation bursts when pools expand under load.
- **Pool expansion edge cases**
  - Guards against missing settings, null pools, and invalid expansion states.
  - Safer fallback behavior when pools need to be created or recovered at runtime.

### Notes

- This release is **backward compatible** for most users.
- Projects that relied on **multiple prefabs sharing the same name** will now behave correctly without requiring changes.
- For best results with time-sliced expansion, ensure **exactly one `PoolManager` exists in the scene**.

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
