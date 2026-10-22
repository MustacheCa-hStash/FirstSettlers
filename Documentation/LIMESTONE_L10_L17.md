# L10–L17 limestone openings and courtyard

Eight additions to the [limestone catalog](../ArtReferences/LimestoneBuildKit/README.md), with an [editable Blender source](../ArtReferences/LimestoneBuildKit/Courtyard/LimestoneCourtyard.blend), [source manifest](../ArtReferences/LimestoneBuildKit/Courtyard/courtyard-models.json) and [Unity overview](../ArtReferences/LimestoneBuildKit/Courtyard/courtyard-unity-overview.png).

| Part | Native dimensions in metres | Menu | Triangles |
| --- | --- | --- | ---: |
| L10 arched limestone doorway | 4 × 3 × 0.50; 1.50-wide opening, spring 1.50, crown 2.25 | Walls | 168 |
| L11 narrow limestone window | 4 × 3 × 0.50; 0.50 × 1 opening, sill 1.25 | Walls | 48 |
| L12 arched limestone window | 4 × 3 × 0.50; 1.50-wide opening, sill 1, spring 1.75, crown 2.50 | Walls | 180 |
| L13 low courtyard wall | 4 × 1.25 × 0.50 | Walls | 12 |
| L14 short courtyard wall | 2 × 1.25 × 0.50 | Walls | 12 |
| L15 chamfered coping | 4 × 0.25 × 0.50 | Walls | 44 |
| L16 courtyard pier | 0.75 × 1.50 × 0.75, cap included | Posts | 56 |
| L17 limestone half-gable | 2 base × 2 rise × 0.50 thick | Roofs | 8 per slope |
| **One of each selectable part** | Opposite gable is a flip variant | | **528** |

Walls use a lower-endpoint origin, X along their span, Y up and Z=−0.25..+0.25. L16 is centred in X/Z and starts at its foot. All parts retain their authored mesh, exact dimensions and UVs through runtime placement. No extra timber post or bearing band increases their dimensions. L17 has two distinct triangular meshes selected with **R**, occupying one menu entry.

## Openings and stonework

L10 and L12 have semicircular openings with a 0.75 m inner radius and 0.25 m radial stone ring. Thirteen individually modeled voussoirs surround each opening, including the central crown stone. Their outer crowns are 2.50 m and 2.75 m respectively, below the 3 m wall top. Squared jamb stones bear beneath the ring. Ordinary coursed blocks stop at the outer ring; internal construction cuts are dissolved inside each block before beveling.

Runtime arch curves use twelve segments with an exact central crown vertex. Each wall uses side solids and convex cap sections above the opening: fourteen occupied hulls for L10 and fifteen for L12, including its lower sill wall. They leave the aperture open in rendering, placement occupancy and pooled physical collision. They never use a single convex collider across the whole arch.

L11 has a clear opening at X=1.75..2.25, Y=1.25..2.25. Its visible lintel is a single 1 × 0.25 × 0.50 m stone at X=1.50..2.50, Y=2.25..2.50, with bearing each side. Four occupied boxes preserve its aperture. The openings contain no glass, shutters or door leaves. Doorway-bottom sockets are omitted across L10's passage, and automatic junction covers respect the authored opening bounds.

## Courtyard heights and roofs

L13 and L14 are 1.25 m tall and may seat directly on terrain or supported construction. L15 adds a real 0.25 m above a wall; its top over L13 is 1.50 m. Its low mesh includes shallow chamfers and the baked surface shows eight capstones. The supplied L15 spans 4 m; a separate 2 m coping variant is not included.

L16 is 1.50 m tall **including its cap**, matching L13 plus L15. Its narrower body is 0.65 m square and 1.25 m high, with a 0.75 m square, 0.25 m-high chamfered cap. Two physical hulls preserve that profile. The pier may ground on terrain and does not need a separate cap above it.

L17 uses a real triangular prism rather than occupying its empty rectangular half. Rising and falling halves span 4 m with a 2 m rise. On a 3 m wall, the structural ridge is at 5 m. Validation exercises attachment to both the installed thatch roof and L33 shingle roof, using their existing eave/ridge datums. L17 supplies masonry infill; no new ridge cover or bargeboard is included.

## Source and material

`Tools/BuildLimestoneCourtyard.py` retains editable, individually beveled stones in the native source and exports nine closed low meshes for eight menu parts. Selected-to-active Blender bakes produce one shared 2048-square colour/AO atlas and tangent-space normal atlas. Stone colour samples the same joint-free limestone trim region as the preceding family. Colour is unlit RGB, with ambient occlusion in alpha; sun and local lighting remain live.

Only each actual stone receives a shallow bevel. The runtime silhouettes retain the arch curves, coping chamfers, pier profile and triangular gables; small joints and tooling are shading detail. A single opaque instanced material uses the existing matte shader, BC7 colour/AO and BC5 normals, mipmaps, trilinear filtering and 4× anisotropy. Existing limestone and timber maps are retained.

## Rebuild and verification

Run Blender with `--background --factory-startup --python-exit-code 1 --python Tools/BuildLimestoneCourtyard.py` to regenerate this family. Preserve hand edits to the source before regeneration. In Unity, use **Tools → Building → Install L10-L17 Limestone Courtyard**. The general catalog rebuild also includes the family when its manifest and FBXs are available. Restart Play mode after import to refresh an active catalog.

`Tools/ValidateLimestoneCourtyard.ps1 -Install` compiles runtime/editor source and uses the isolated limestone Unity project. It imports twice to verify stable GUIDs, checks unchanged unrelated definitions, bounds, normal/tangent bases, UVs, closed collision hulls, menu metadata and actual-mesh thumbnails, and captures front/reverse URP views plus the actual instanced renderer.

Play-mode checks cover all eight parts at all eight headings, sixteen actual CharacterMotor doorway crossings, twenty-four TerrainCollider boundary/pier cases, unsupported floating placements, coping height and support removal, both gable orientations and both roof families. Installed timber/infill, L01–L03, L04–L09, ordinary-stone and modular-wood regressions run before selective installation. The installer appends eight catalog references and copies only this family's assets. Saved world scenes are not modified.

Evidence: [new family](../ArtReferences/LimestoneBuildKit/Courtyard/unity-validation.json), [timber/infill](../ArtReferences/LimestoneBuildKit/Courtyard/timber-regression.json), [L01–L03](../ArtReferences/LimestoneBuildKit/Courtyard/l01-l03-regression.json), [L04–L09](../ArtReferences/LimestoneBuildKit/Courtyard/l04-l09-regression.json), [ordinary stone](../ArtReferences/LimestoneBuildKit/Courtyard/stone-regression.json), [modular wood](../ArtReferences/LimestoneBuildKit/Courtyard/modular-regression.json), [arch front](../ArtReferences/LimestoneBuildKit/Courtyard/L10-unity-front.png), [arch back](../ArtReferences/LimestoneBuildKit/Courtyard/L10-unity-back.png) and [instanced capture](../ArtReferences/LimestoneBuildKit/Courtyard/courtyard-instanced.png).

The final isolated run passed **5,011 new assertions**, **41,112 timber/infill**, **1,414 L01–L03**, **3,382 L04–L09**, **3,558 ordinary-stone** and **28,311 modular-wood assertions**: **82,788 total**. The final front/reverse and native-source renders were visually inspected. Source and editor compilation passed with existing obsolete-API warnings. The catalog now contains 49 selectable entries, with the previous entries retained and eight appended; the second L17 slope is a flip rather than an extra tile.
