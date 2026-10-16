# Sculpting session lifecycle and elevated-spawn recovery

October 15, 2026. User follow-up after P6 A/B: the character could neither walk nor fall from an elevated respawn, and terrain edits should not persist between playtests while the rest of the world has no save system.

## Active world policy

`WorldManager.BuildConfiguration()` now supplies no `TerrainEditStorageConfiguration`, regardless of sculpting enablement or seed. Consequently the active world creates no terrain store, performs no disk load, autosave or shutdown flush, and never opens the previous development slot. Existing files are ignored and left intact. There is no persistence toggle to accidentally retain from an older serialized scene.

Committed density and its vegetation suppression remain in memory for the lifetime of that world. Leaving and returning to an edited area within a session reconstructs the excavation. Stopping Play Mode or recreating the world starts with the procedural baseline, including when the seed is unchanged. Changing the seed also starts clean. The HUD explains this lifecycle and hides the F10 save instruction; an F10 command reports that disk saving is disabled.

The tested P4 persistence backend remains available for deliberately configured hosts and future whole-world saving, but is disconnected from normal play. Earlier checkpoint reports describe that backend's capabilities, not the current world's policy.

## Freeze diagnosis and recovery

The elevated-spawn case was reproduced against a workspace copy of the user's previous terrain data. The copied manifest contained revision 88 and 155 density owners. Starting at (-244, 100, 366), the real CharacterMotor fell to approximately Y=82.17 and then stopped. A successor terrain coverage batch exceeded the remaining portion of the existing 128 MiB derived budget while the old representation remained alive. Required collision coverage could not advance, so the movement guard correctly blocked both translation and gravity; no subsequent progress could release it.

The runtime now attempts one bounded recovery after a capacity failure:

1. Dispose the failed batch before releasing its optional CPU extraction cache. Keep published density, render meshes and colliders intact.
2. Retry the same pending operation without retaining another extraction cache. Prepare the normal required collision coverage first; in this recovery batch, retain edited rendering only within **16 m of the focus** or intersecting an occupied/support pin. Remote edited appearances temporarily use the baseline representation. Normal 192/256 m rendering distances are unchanged outside this pressure fallback.
3. Publish the successful reduced representation atomically, then resume normal coverage on later updates. Density and foliage suppression are never discarded by this fallback.
4. If the reduced batch still cannot fit, report failure and stop retrying. Do not exceed the budget, alter accepted density or remove the old collider. F3 reports the number of cache-pressure recovery attempts.

Approach through air remains possible with a reduced visual set. The guard blocks entering an edited volume whose visible representation is unavailable, and the occupied pin requires that representation in a subsequent batch. This prevents treating an invisible excavation as ready terrain.

In the final copied-data reproduction, one recovery released the guard and the motor continued to the terrain at approximately Y=49.41, then walked 2.357 m. Its committed density remained at revision 88. The new session-only policy additionally prevents old saved coverage from participating in startup at all. No player spawn height, scene setting, gravity constant, collision safety rule or memory cap was relaxed.

## Validation

- Runtime, editor and player compilation through `Tools/ValidateTerrainSculpting.ps1`.
- A native `ChunkManager`/`CharacterMotor` case passed: it fell from 8 m above ready terrain, landed on the source collider and walked 4.85 m with the terrain guard enabled.
- Actual WorldManager configuration checks session-only behavior across seed changes. In-session retirement/return retains excavation and foliage suppression; a new runtime begins with neither.
- A deliberately unsatisfiable successor allowance checks that recovery stops after one retry, preserves density and usable published collision, and releases reservations.
- Existing mesh, transaction, persistence-backend, interaction, performance and P6 vegetation suites passed in the same isolated project. The production-baseline fixture now includes the scene's authored snow-blend gamma when calculating the complete generation identity.

Commands and logs:

```powershell
Tools/ValidateTerrainSculpting.ps1 -MeshChecks -IntegrationChecks -PersistenceChecks -InteractionChecks -PerformanceChecks -WorldChecks
```

The command log is `.utmp/terrain-sculpting/session-fix-command.log`; the Unity regression log is `.utmp/terrain-sculpting/validation.log`. The original recorded-case observation is `spawn-repro.log`, and the final successful recovery/landing/walking observation is `spawn-recovery-final.log`, in the same workspace directory. These are isolated fixtures, not a claim about live scene frame-time percentiles. Heavy-edit performance gates remain in P7.

Building-support options and cave ambient lighting remain deferred as previously requested.
