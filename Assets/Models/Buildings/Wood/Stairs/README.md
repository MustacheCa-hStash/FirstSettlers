# W21 stair source

Current source: **HalfStoryStair_1.25Wx1.5Hx2.0D.fbx**.

Blender dimensions: X=1.25 m wide, Y=2 m horizontal run, Z=1.5 m high. Unity dimensions after conversion: X=1.25, Y=1.5, Z=2. Keep metre scale and applied object transforms. The runtime lower-front-left origin is (0,0,0), with the low foot at Z=0 and upper tread at Y=1.5 in the last 0.25 m of the +Z run. The current import already meets this contract.

Use the existing ordinary wood trim atlas and a single opaque wood material. UV islands can overlap; retain UV0. Treads, stringers and pegs are visual geometry. The independent walking collider is a smooth convex ramp, so extra mesh detail does not add physical tread edges.

After re-exporting, use **Tools > Building > Link or Rebuild W21 Stair**. It updates the derived mesh, preserves the source, and registers the option once. **Prepare W21 Stair Import** can stage collision/definition assets before a source is available. The installer skips menu registration when the model is absent.

See [dimensions, collision approach and validation](../../../../../Documentation/W21_STAIR.md).

