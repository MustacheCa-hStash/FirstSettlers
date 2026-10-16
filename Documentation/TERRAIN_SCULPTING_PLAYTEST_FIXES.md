# Terrain sculpting: shutdown and sustained excavation fixes

October 15, 2026. Follow-up to the P5 playtest. Scope: shutdown reliability and excavation latency only. P6 foliage, building support, exposed-stone materials and ceiling shadows remain deferred. Scene settings and existing terrain save files are left as found.

## What changed

**Shutdown:** the HUD/brush view checks Unity object lifetime before hiding and disposes at most once. The controller no longer hides twice during Exit. WorldManager uses finally blocks so controller cleanup cannot prevent backend disposal/shutdown saving. Regression cases destroy the HUD first, outline first, and parent first, then repeat hide/unbind/disposal.

**Seam construction:** packed vertex-pair edge keys now use a hash that mixes both vertex indices. The old default ulong hash XORs its halves; sequential mesh indices produced many colliding buckets. Larger meshes therefore incurred superlinear dictionary work while identifying their boundary edges. The geometry and seam algorithm are preserved.

**Local extraction:** derived geometry is cached per 16-cell mesh brick (4.8 m at the existing 0.3 m spacing). Entries include their exact cropped cell bounds, authoritative/visual field identity, and visual coverage anchor. The immutable identities of the 27 sample owners touched by a brick's 19-cubed value/gradient halo validate reuse. These identities are process-local, never saved. Entries retain stamps and geometry, not old density owners or snapshot leases. A neighboring edit invalidates shared positions and normals conservatively.

**Local assembly:** an excavation group remains a coverage/seam envelope. It is no longer the unit of mesh extraction/assembly. Each changed source chunk extracts only its own window. Outside group edges retain the fine/coarse baseline collars; interior source-chunk borders are shared volumetric faces and can contain multiple cave floors/openings. Distant source chunks in the same connected cave keep their existing Unity render meshes and colliders. A two-cell sample/normal influence margin invalidates both chunks when needed at a border. Growing/moving group collars and changing visual coverage also invalidate affected chunks.

Render and collision reuse identical authoritative brick extraction where their cell bounds match. Their different outside collars still receive separate geometry. Source-chunk output uses exact-size array concatenation, avoiding additional growable lists/copies and repeated normalization. Temporary patch arrays are released from the admission estimate after each source chunk is assembled.

**Budgets:** the derived cache has a conservative charge cap of one eighth of the configured derived budget: 16 MiB under the existing 128 MiB default. Payloads, stamp/index metadata and staging are charged within that existing budget. Previously published caches are read-only while a worker builds a successor; only a successful atomic density/render/collider publication installs it. Failed/stale work cannot publish a cache. Cached geometry is expendable, bounded, and cleared with released coverage/runtime disposal. Density encoding, 64 MiB authoritative payload budget, sample spacing, render distances and save format are unchanged.

F3 now reports extracted/reused mesh-brick requests and extraction/assembly times, alongside worker, staging, cooking, commit and total edit time. Reuse counts include multiple requests for the same brick, including rendering/collision sharing; they are not a count of distinct stored density bricks. New profiler markers: `FS.Streaming.Sculpt.ExtractBricks` and `.Seams`.

## Measurements

The isolated benchmark seeds connected roofed rooms with 4, 16 or 36 radius-2 m excavations spaced 3 m apart, reconstructs as from a save, then commits two radius-0.5 m cuts and one radius-2 m cut through the real asynchronous runtime and Unity collider publication. The surface is a deterministic slope. Both versions produce the same final render/collision triangle counts.

Original implementation, before these changes:

| Seeded room | Small cuts, total latency | Large cut, total latency |
| --- | --- | --- |
| 4 excavations | 2,339–2,427 ms | 2,556 ms |
| 16 excavations | 16,500–16,614 ms | 17,226 ms |
| 36 excavations | 44,228–44,902 ms | 46,178 ms |

Final combined regression run:

| Seeded room | Small cuts, total latency | Large cut, total latency | Small-cut brick requests, extracted/reused |
| --- | --- | --- | --- |
| 4 excavations | 109–125 ms | 126 ms | 60 / 190 |
| 16 excavations | 174–266 ms | 298 ms | 60 / 430 |
| 36 excavations | 217–327 ms | 342 ms | 60 / 580 |

The native SmearScene generator/ChunkManager fixture measured a 125 ms first scoop and five subsequent scoops at 78–95 ms in the final combined run. This uses small, content-free coverage, not the user's running scene.

These are individual observations, not p95 estimates or a live FPS result. Repeated runs vary with allocations, GC and other work. Earlier repeated dense-room measurements included a small cut around 420 ms. The <=150 ms ordinary-brush p95 target is still open for heavily edited/live scenes. Work to assemble/upload/cook an affected 38.4 m source chunk still grows with its local geometry; this change does not make every part of publication constant cost. Dense-room indivisible commits were approximately 36 ms in the combined run, so frame-time verification remains required. Further mesh-object subdivision/incremental collision publication can be evaluated at P7 if those costs dominate the real playtest.

An initial cache prototype exhausted the derived admission budget on larger cases. The delivered version uses bounded cache retention, per-chunk temporary ownership and exact-size assembly; the largest small/large-cut cases and 80 m cave/border-edit cases pass under the original 128 MiB cap.

## Validation and reproduction

Run `Tools/ValidateTerrainSculpting.ps1 -MeshChecks -IntegrationChecks -PersistenceChecks -InteractionChecks -PerformanceChecks`. It compiles runtime/editor/player sources, prepares the isolated Unity project, and runs the density, topology/motor, streaming/transaction, persistence, interaction and performance fixtures. It does not open or modify SmearScene. Unity needs access to its existing licensing service.

- P1: 8,565 assertions and canonical height agreement.
- P2: 613,404 assertions, 16 real motor traversals, fine/coarse collars, buried cavity and native-height seams.
- P3: transaction/rollback, stale/cancellation/shutdown, source/far handoffs, memory failure, range/pin retirement, movement and published source-chunk topology pass. Polling assertion counts vary by timing.
- P4: 446,934 assertions, shutdown flush/native restart, same reconstructed geometry, real-file recovery and corruption/mismatch handling.
- P5 interaction: 48 checks, including the three new lifetime regression cases.
- Performance: cached/uncached geometry matches exact triangle positions and winding with matching normals, including fine/coarse collars, changed density, cropped source windows and visual fade anchor changes. An unchanged field extracts zero new mesh bricks in the cache fixture.
- Connected 80 m cave: a local cut retains distant Unity meshes/colliders; a border cut replaces both neighbors and preserves the remote roof collider.
- Sustained rooms: all nine timed small/large transactions complete with bounded reservations and the original derived cap.

Logs remain under `.utmp/terrain-sculpting`: `performance-before.log`, `validation.log`, and `performance-final.log`. The first records the old implementation; validation records the combined suite; the last repeats cache/fade and sustained-workload checks. Existing obsolete-API warnings and isolated foliage resource fallback warnings remain unrelated to these changes.

For the next manual check, use the existing save/cave, compare small and large cuts at several locations, include a source-chunk boundary if available, and exit Play Mode with sculpt mode active. F3's worker/extraction/assembly/commit counters now separate any remaining delays. No additional user profiler capture is required to use this build. Grass/foliage suppression, wall material mapping, exposed stone and underground shadow changes remain P6.
