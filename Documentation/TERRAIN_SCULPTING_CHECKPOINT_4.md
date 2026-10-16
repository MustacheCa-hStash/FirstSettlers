# Terrain sculpting checkpoint 4: persistence and recovery

October 15, 2026. P4 implements the dedicated local terrain sidecar from the [approved plan](TERRAIN_SCULPTING_PLAN.md). Development digging controls remain P5. Sculpting remains **off in the saved scene**; the live walking baseline and native edit-latency gate from P3 remain open.

## Save and restart flow

`WorldManager` supplies `Application.persistentDataPath/Worlds/development-default/TerrainEdits/` when sculpting is enabled. `WorldConfiguration.TerrainEdits` can supply another explicitly named slot/root for a host or fixture; null means memory-only. There is no building, inventory or player-position save here. Disabling the feature neither loads nor erases the sidecar.

1. Startup opens the slot with an exclusive lifetime writer lock. One background IO task validates the manifest, world identity, profile and every referenced brick. Only a complete valid root is installed into the fresh density world. Partial reads never become authoritative.
2. While validation is pending, excavation and movement are blocked. After loading, the P3 worker reconstructs halos, replacement skin, underground surfaces and collision from the restored density plus the existing canonical generator. Movement remains blocked until initial edited coverage is published. No saved mesh or collider is needed.
3. Committing an excavation changes the in-memory revision. A two-second quiet interval starts when the persistence service observes that revision. One background save owns a stable committed snapshot; another edit can commit while that snapshot is being written. Pending brush overlays are explicitly rejected by the store.
4. The writer compresses canonical brick payloads, writes new immutable files and flushes them to disk. It reads those files back to validate them. Unchanged owners reuse existing records, which are also checked before publication. It writes and validates a complete successor manifest, then atomically replaces the active manifest while retaining its predecessor.
5. `TerrainEditPersistence.RequestSave()` asks for a nonblocking save. `Flush()` waits for an in-flight save and saves any newer committed revision. Runtime shutdown first cancels/joins pending mesh work, then flushes the last committed density before releasing it. A failed flush is reported; it is not described as durable.

F3 includes save status, saved revision and the dirty flag alongside the existing terrain diagnostics. These APIs/status fields are ready for P5; no controls were brought forward.

## World compatibility

The slot has a stable SHA-256 world ID independent of the seed. The generation fingerprint includes the format version, explicit generator/interpolation version, all `WorldGenerationConfiguration` fields, nested water and erosion fields (including the erosion schema version), world scale, density spacing/band/brick size, sign convention and encoding rule. Canonical binary field ordering avoids locale-dependent text and unstable runtime hashes.

`TerrainEditIdentity.GeneratorVersion` must be bumped when generation algorithms, baseline interpolation or the solid-underground policy change. A future procedural cave generator therefore requires a deliberately different fingerprint or an explicit migration. Rendering distances/materials do not change authoritative density. Unknown formats and identity/profile mismatches stop loading and preserve all save files; there is no automatic reset or migration.

## File format, version 1

All numeric fields are little endian. Checksums are lowercase SHA-256 hex stored as 64 ASCII bytes.

| File | Contents |
| --- | --- |
| `manifest.bin` | Magic/version; world ID and fingerprint; spacing and band as doubles; committed int64 revision; int32 owner count; sorted canonical entries; SHA-256 of preceding bytes. Header is 164 bytes, each entry 85 bytes, checksum trailer 64 bytes. |
| Manifest entry | Three int32 brick coordinates; int64 record revision; uniform/detailed byte; 64-byte digest identifying the immutable record. |
| `bricks/<digest>.brick` | Magic/version; canonical coordinates; record revision; encoding and codec bytes; decoded/compressed lengths; decoded-payload SHA-256; compressed payload. The manifest/filename digest validates the entire record, including its header. Header is 102 bytes. |
| Payload | Encoding 0 is one signed int16 uniform override; encoding 1 is 4,096 signed int16 samples in canonical X-fastest order. Codec 1 is built-in `.NET DeflateStream`, lossless. No invalid `-32768` codes are accepted. |
| `manifest.previous.bin` | Previous complete manifest, kept by atomic `File.Replace`. Its referenced records are never overwritten. |
| `writer.lock` and temporary files | Lifetime writer exclusion and unpublished writes. Temporary/orphan brick records are ignored on load. |

Uniform air is a real persisted override. Missing owners use the procedural baseline. Transient 19-cubed halos, render-distance fades, Unity objects, mesh data and brush history are not serialized. An individual detailed owner has 8 KiB of raw samples; compression varies with the field.

The prototype caps manifests at 100,000 entries / 16 MiB and record files at 16 KiB. A decoded record is exactly 2 or 8,192 bytes; decompression checks for both truncation and extra output. Addresses, revisions, duplicate coordinates, lengths, codec and checksums are validated before root publication. Malformed or oversized files cannot request unbounded density allocation.

## Memory and performance decisions

- The total density payload/snapshot/codec admission budget remains **64 MiB**. Persistent hosts reserve headroom for a complete conservatively charged snapshot and 64 KiB of codec scratch. Consequently accepted committed payload is capped at **31.96875 MiB**. The edit is refused before publication if it would exceed this limit; the accepted world can still be saved once other transient work drains.
- This headroom is a P4 implementation refinement, not an increase to the approved memory budget or a change to resolution. Memory-only P1–P3 fixtures retain their original admission policy. An older store exceeding the persistent-host resident limit fails safely at load.
- Both saved and unsaved canonical overrides remain resident in this prototype. Retiring distant meshes/collision cannot evict them. There is no disk paging of authoritative density yet; large worlds will eventually need that separate cache/index design. The capacity test verifies that accepted data still flushes after the next edit is refused.
- Compression/validation runs on one background IO task, separate from the existing shared mesh/collider slots. Snapshot capture and the small status transitions run on the calling main thread. Shutdown/explicit blocking flush can wait for IO.
- Incremental saves write changed brick records and a full manifest. They still validate the referenced unchanged files; IO cost therefore grows with the saved owner count. Weak source references identify unchanged owners without retaining obsolete payload outside density accounting.
- The 64 MiB figure is a payload admission limit, not total process memory. Manifest/index dictionaries, bounded file metadata buffers, ordinary terrain caches and Unity allocations are separate. P3's separate 128 MiB derived staging estimate is unchanged.
- No record garbage collection is implemented. Predecessor records and interrupted-write orphans accumulate on disk. This preserves recovery safety, but disk use grows with edits/saves rather than only current live payload.

The small storage fixture has 29 owners (one uniform), 229,378 resident payload bytes, a 9,519-byte first save and a 3,952-byte incremental save. These include newly written record/manifest bytes and illustrate compression/reuse for that fixture only; they are not representative whole-world disk or latency budgets.

## Failure policy

- Interrupted record/temporary-manifest writes leave the old manifest authoritative. A crash after manifest replacement loads the entire new revision. No delete-then-move fallback is used if atomic replacement fails.
- A corrupt latest manifest or referenced brick can recover the previous **complete validated** manifest. Recovery reports the selected older revision and makes the session read-only: excavation and saving are disabled, while reconstructed terrain can be inspected. It does not silently continue saving over damaged evidence.
- If neither generation is valid, or the identity/format is incompatible, loading remains blocked. Movement and excavation cannot proceed against a silently substituted baseline. A valid running world is never partially replaced by a failed load.
- Disk/write/admission failures retain committed live density and its dirty revision. Further excavation is blocked until an explicit retry succeeds. Existing movement/geometry remain usable after a save failure. F3 and a one-time failure report expose the error.
- A second process cannot acquire the same slot. Changes to the active manifest outside the session are detected before a successor is published. There is no automatic reset UI in P4.

Atomic publication plus flushed records covers the tested process-interruption boundaries. It is not a guarantee against hardware/filesystem failure or a crash losing edits newer than the last completed save. Tests inject failures at real file-operation boundaries; they do not power-cycle the machine.

## Validation and checkpoint limits

`Tools/ValidateTerrainSculpting.ps1 -MeshChecks -IntegrationChecks -PersistenceChecks` compiles complete runtime/editor/player source and runs isolated Unity fixtures. The new editor menu is **Tools > Terrain > Validate Sculpting Persistence and Recovery**. Test sidecars live under the repository's `.utmp/sculpt-p4/`; the main scene and actual development save slot are untouched.

The final combined run passed runtime/editor/player compilation (existing project warnings), 8,565 P1 assertions, 613,404 P2 assertions with 16 actual motor traversals, 1,925,007 P3 assertions and **446,934 P4 assertions**. The P3 count includes asynchronous polling and varies slightly per run. Evidence is in `.utmp/terrain-sculpting/validation.log`; the command output is `p4-command.log` in that directory. The native batch fixture uses small content-free coverage, not a rendered Play Mode session; missing authored foliage resources in the isolated project are unrelated to persistence validation.

P4 checks cover byte-equivalent detailed/uniform density, negative/cross-brick coordinates, reconstructed halos, identical mesh positions/normals/connectivity, record reuse, pending-overlay rejection, interruption before/after manifest publication, an interrupted first save, checksum/truncation/unknown-format/profile handling, read-only predecessor recovery, exclusive slot ownership, save headroom, failed-save retry, a newer edit behind an in-flight save and orderly shutdown.

Runtime fixtures restart inside an excavated tunnel, verify the reconstructed floor collider and movement guard, retire/return to edited coverage without losing density, and block corrupt startup. A separate native `ChunkManager` fixture saves a SmearScene-profile scoop through shutdown, creates a fresh host, and compares the published Unity geometry and physical floor hit. It also verifies that disabling sculpting preserves the files.

P3's native edit latency still exceeds the provisional 150 ms warm target (the final combined run measured five subsequent native scoops at approximately 222–427 ms); this phase does not optimize meshing or claim a live frame-rate result. P5 development interaction is the next phase. P6 retains foliage/building/material integration; generated caves, ore and AI navigation remain deferred.
