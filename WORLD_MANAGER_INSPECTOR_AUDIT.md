# World Manager inspector cleanup

Scope: World Manager and its nested generation settings. Material inspectors and shader assets are deferred until user review.

## Removed controls

| Serialized setting | Why it was obsolete |
| --- | --- |
| `foliageParent` | Passed into FoliageManager and stored without being read. Foliage is parented under chunk runtimes. Constructor compatibility slots remain for existing callers. |
| `treeSettings.treeCellSize`, `treeSpawnChance`, `treeMinDistance` | No consumers. Tree positions come from WorldFeaturePlan and biome-specific placement rules. |
| `grassSettings.grassInstanceDataPropertyName` | Current ResidentGrassRenderer CPU/GPU rendering uses the fixed `_GrassInstanceData` shader contract. The override reached the retired per-chunk renderer. |
| `grassSettings.enableBillboardRenderFade`, `billboardRenderFadeDuration`, `billboardFadeDitherPixelSize` | The retired per-chunk billboard path is disabled in FoliageManager. GrassStream/ResidentGrassRenderer use representation transitions and outer-distance fades. Removed the unused material clone associated with these controls. |
| Hidden `grassSettings.billboardUniformScaleRange`, `randomizeBillboardYaw`, `billboardSeedOffset` | No consumers. Far grass uses the same candidates, scale and seed as near grass. |

The remaining hidden legacy grass grid/queue fields still have internal consumers and compatibility validations. They are retained; this cleanup does not replace that internal renderer code.

## Clarified working controls

- Organized root settings into foldable sections. Future serialized root fields still appear automatically if not assigned a section.
- Climate Octaves displays the actual sampled count. ClimateGenerator subtracts two from the stored value; changing the inspector count converts back to that existing representation. Opening the inspector does not migrate or rewrite values. Persistence and lacunarity are inactive at one sampled octave.
- Tree Color Seed Offset names its actual role: tree leaf/bark tint variation, not placement.
- Boundary Chunk Height Grid Resolution affects individual far chunks. Quadtree patches retain their existing fixed 33/65-point geometry grids.
- Far Control Map Base Resolution explains the existing macro-tile scaling and 128-point cap.
- Grass distance and job scheduling labels describe the current GrassStream behavior.
- Inactive controls are greyed out with explanations: disabled far terrain/profiling/foliage, unused water reflection controls, disabled GPU shader overrides, grass-derived scatter distances/fern LODs, disabled distance densities, distant-tree overrides and feature-specific erosion controls.
- Working tree billboard ring fallbacks remain editable when Distant Trees is off. Base landform settings and Max Mesh Spacing remain editable when erosion is off.
- The regeneration button supports selected active World Managers during Play mode. Startup/cached settings guidance and existing tooltips explain when regeneration or a Play-mode restart is needed.

## Serialization and verification

No scene, prefab, material, texture or shader assets are rewritten. Existing scene YAML can contain removed field names until Unity next saves it; Unity ignores those fields. Surviving setting names and storage remain unchanged.

`Assets/Editor/WorldManagerInspectorValidation.cs` provides **Tools > Terrain > Validate World Manager Inspector** for schema coverage, removal and conditional-control checks with real SerializedObjects, including mixed selections. Its batch entry point additionally loads SmearScene in the disposable validation project, checks inspection leaves serialized values unchanged and terrain material/viewer references intact, and runs the existing resident grass CPU/GPU streaming regression suite.

Unity 6000.4.8f1 batch verification passed on 2026-10-03:

- Compilation succeeded; 65 remaining root fields were grouped exactly once and all 11 removed fields were absent from serialization.
- Dependency toggles and mixed selections passed. SmearScene inspection traversed 610 visible property paths without changing serialized values.
- Resident grass regression passed: bounded-job publication, cache/re-entry, settings invalidation, empty results, distance selection, CPU/GPU transition agreement and partial resident-buffer updates.
- `git diff --check` passed. Live inspector appearance remains for the user's review.
