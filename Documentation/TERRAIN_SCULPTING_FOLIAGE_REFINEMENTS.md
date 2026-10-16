# Surface foliage after excavation

Follow-up to the P6 playtest: tighten the bare strip around an opening and preserve grass above a tunnel whose roof remains intact. This supersedes the original conservative XZ projection policy in P6/A13.

## Current behavior

- Grass and small decorations are suppressed where their original landscape support has been excavated, plus **0.15 m** of circular padding.
- The mask uses **0.15 m cells**, independently of the unchanged **0.3 m density lattice**. It does not expand every removed voxel into all adjacent terrain cells. Rasterization still makes the exact edge vary by part of a mask cell.
- A probe **0.05 m below the original surface** must change from solid to air before its mask cell is marked. Small surface shifts within this tolerance can retain grass. Underground excavation that preserves that support cannot project a clearing onto the roof.
- Extending a tunnel from an existing entrance does not enlarge the clearing unless the extension also changes support at the surface. Breaking through the roof clears foliage at the new opening.
- The same mask controls resident and pending grass, ferns, flowers, small bushes and other small decoration. Trees/large rocks retain their existing behavior. This does not implement building support.

The old policy projected any removed stone into XZ, expanded adjacent cells, and added a square 0.6 m margin. That caused both the broad border and a surface clearing matching the outline of a buried cave.

## Reconstruction and cost

The worker visits XZ cells influenced by changed density owners, deduplicating columns across vertical owners. It probes four finer mask-cell centers per terrain cell. Density interpolation uses the same six tetrahedra and encoded corner values as the terrain mesher, so the support test follows the reconstructed field rather than an ideal brush shape or a raycast against stale colliders.

Only changed columns need evaluation after an edit. Coverage-only batches reuse the same immutable mask. Temporary sample/height dictionaries are charged to the existing derived budget and released from the admission estimate before geometry construction. Steady frames still perform no density scans or grass source uploads for unchanged mask tiles.

A 16×16-bit tile now spans 2.4 m square. At equal fully suppressed area the finer mask can require about four times as many tiles as the original 0.3 m mask, but sealed caves create no clearing tiles. The existing conservative 16 MiB mask cap remains part of the 128 MiB derived budget. Mask publication remains atomic with terrain and collision; rejected or pending edits cannot clear foliage early.

Excavation-only edits make surface support loss monotonic, allowing immutable tile sharing and union with previous clearings. Filling will need explicit mask recomputation/restoration when that feature is added. The mask is derived from density and is never an independent save format. Normal play remains session-only; a new play session starts clean. Explicit P4 persistence hosts can reconstruct the refined mask from existing density.

## Validation

CPU coverage includes a fully buried cavity, a 0.6 m intact roof, actual breakthrough, tunnel extension below an existing entrance, sloping ground, negative owner/source-chunk seams, a tight box-cut border, immutable publication, resident/late vegetation, retirement and optional-backend reload.

The final focused run passed 295,335 surface/vegetation checks and the cache/local-rebuild/sustained-excavation performance suite. The nine small/large room-edit transactions completed in approximately 111–348 ms in the isolated fixture; this is not a live frame-time percentile. Runtime, editor and player compilation passed. The earlier integration and interaction checks passed as well.

The native GPU fixture contains three plants: one over an entrance, one on the retained roof of the same cave, and one unrelated plant. Grass and fern source/culling buffers retain exactly the latter two after suppression. Existing stone rendering, roof shadows and exterior appearance checks remain in that fixture.

Native GPU/render validation passed, including 98 terrain shader variants, the material inspector regression and existing resident-grass culling coverage.

Commands:

```powershell
Tools/ValidateTerrainSculpting.ps1 -IntegrationChecks -InteractionChecks -PerformanceChecks -WorldChecks
Tools/ValidateTerrainSculptingWorld.ps1
```

The final focused CPU/performance run is `Tools/ValidateTerrainSculpting.ps1 -WorldChecks -PerformanceChecks`. Its Unity log is `.utmp/terrain-sculpting/validation.log` and command output is `foliage-surface-final-command.log` in the same directory. Native GPU output is `p6-graphics.log`; its command output is `foliage-surface-graphics-command.log`.

Restart Play Mode before comparing the new border with the old one; filtering cannot restore already removed resident instances inside an old running session. Cave ambient lighting and building-support work remain deferred.
