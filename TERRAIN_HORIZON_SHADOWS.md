# Terrain horizon shadows

Terrain now receives broad mountain/valley shadows from procedural height data,
in addition to URP object shadows. Horizon visibility is independent of URP's
140-unit PC shadow distance. Terrain renderers still do not cast into URP's atlas.

## Configuration

WorldManager > Terrain Lighting > Terrain Horizon Shadows controls the feature.
Settings are captured on world creation: restart Play mode or use Regenerate
Terrain after changing them. SmearScene enables these defaults:

- Search Distance: 6,000 **world units**, not terrain coordinates.
- Strength: 0.8; Softness Degrees: 3; Height Bias: 1.5 world units.
- Shadow Tint: blue-gray (0.45, 0.52, 0.65); Tint Strength: 0.5. Set tint
  strength to 0 to preserve the original ambient brightness in shadows. Tint
  multiplies the existing shadowed lighting before fog, with no extra samples.
- Eight azimuth directions and 32 logarithmically spaced obstruction distances.
- Near chunks: 5 x 5 horizon grid (9.6-world-unit spacing in SmearScene).
- Far 4/8/16-chunk patches: 17 x 17 (9.6/19.2/38.4-unit spacing).
- Far 32-chunk patches: 33 x 33 (38.4-unit spacing).
- Shared CPU height cache: 65,536 samples; retained inactive horizon tiles: 128.

## Generation and rendering

Attaching a mesh binds its world footprint to the shadow service. Geometry and
colliders become usable immediately; they never wait for shadow data. One
ThreadPool task generates the nearest bound pending footprint after near terrain,
mesh and collider workers settle. Far terrain can continue generating alongside
it. Cache hits and LOD changes do not regenerate an existing footprint.

The worker imports exact world-aligned samples from the chunk's padded heightmap
or far height grid. Searches use bilinear reads from a progressively coarser
world-aligned lattice. Missing lattice samples are deduplicated and evaluated
once in a Burst job on that same worker, using the world's final height sampler,
including erosion, river shaping, seed and height scaling. Adjacent footprints
share these samples; searches cross unloaded/off-screen regions without creating
terrain meshes. The lattice caps at 1,024 terrain units (307.2 scene units in
SmearScene) for the most distant blockers.

Each receiver stores eight maximum obstruction elevation angles as two linear
RGBA8 textures, with bilinear filtering, no mipmaps and no retained CPU pixel
copy after upload. Tile corners map to texel centers, so matching near/far edge
samples agree. The worker bounds its height cache after each request (temporary
miss data for the current request can exceed the retained limit). Unbound tiles
are evicted by last use; currently bound textures remain alive. Pooling removes
material bindings; regeneration/disposal cancels and discards old worker results.

New textures fade in over 1.5 seconds. The vertex shader calculates the current
main light's azimuth/elevation; the fragment shader takes two texture reads,
interpolates directional horizon angles and smoothly attenuates direct lighting.
The optional tint darkens/colors ambient and direct illumination only inside
the procedural shadow mask; sunlit terrain and fog remain intact. The same maps
respond to the sun or moon without regeneration. Receive Shadows bypasses both
URP and horizon visibility/tint.
The PC Forward+ light-loop variant is included so main-light attenuation works
with PC_Renderer. No additional sampler registers or shadow draw calls are used.

## Limits and validation

This approximates broad terrain shadows, not trees, buildings, overhangs or
narrow gullies. Coarse height/direction sampling can miss thin ridges or shift
shadow edges, especially at sunrise/sunset. Softness hides coarse transitions
but does not recover missing blockers. An unloaded blocker outside the search
distance contributes nothing. Generation is cached in memory, not on disk.

Tools > Terrain > Validate Horizon Shadows (or batch execute method
TerrainHorizonShadowValidation.RunBatch) checks flat terrain, off-tile mountain
occlusion, compass directionality, negative coordinates, near/far seams, real
background/Burst sampling, bounded cache retention, pooled bindings, shader
variants and GPU light/shadow behavior. Profiler markers:
FS.Streaming.Worker.TerrainHorizon and FS.Streaming.ApplyTerrainHorizon.
