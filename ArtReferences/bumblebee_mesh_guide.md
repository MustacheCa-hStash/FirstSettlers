# Bumblebee mesh tracing guide

Use [bumblebee_mesh_guide.png](bumblebee_mesh_guide.png) as a **top orthographic** Blender reference. Coordinates below are in Blender's XY plane: head is **+Y**, body centerline is **X = 0**. The reference drawing uses **500 pixels per Blender unit**. Model the right half, then mirror across X = 0.

This is a compact, soft bumblebee silhouette. Body length is about **1.28 units**; widest body span is **0.70 units**; total spread-wing span is about **2.16 units**. Scale the finished object later as needed.

## Body outline

Connect these vertices in order from head center to rear center. Mirror the interior vertices to make the left edge, then close the shape. The small inward step at Y = 0.29 separates head and thorax; the step at Y = -0.18 separates thorax and abdomen.

```text
(0.00,  0.58)  (0.09,  0.57)  (0.15,  0.53)  (0.19,  0.47)
(0.19,  0.39)  (0.16,  0.32)  (0.14,  0.29)  (0.21,  0.27)
(0.27,  0.21)  (0.30,  0.12)  (0.31,  0.01)  (0.29, -0.10)
(0.25, -0.18)  (0.29, -0.21)  (0.34, -0.30)  (0.35, -0.41)
(0.32, -0.52)  (0.25, -0.62)  (0.14, -0.68)  (0.00, -0.70)
```

Optional cross edges go from the right outline to X = 0 and then to the mirrored left outline at these XY positions: `(0.19, 0.39)`, `(0.14, 0.29)`, `(0.30, 0.12)`, `(0.29, -0.10)`, `(0.25, -0.18)`, `(0.34, -0.30)`, `(0.32, -0.52)`. Give the body a rounded upper profile; the thorax should be the thickest part. The abdomen should remain blunt and plump rather than pointed.

## Right forewing

Make a **separate flat mesh island**. Connect vertices 1–12 in order and close 12→1. Vertices 1 and 12 extend under the thorax. The left forewing is the X-mirrored copy.

```text
 1 (0.15, 0.14)    2 (0.33, 0.24)    3 (0.54, 0.31)
 4 (0.78, 0.37)    5 (0.98, 0.35)    6 (1.08, 0.29)
 7 (1.07, 0.22)    8 (0.98, 0.17)    9 (0.78, 0.12)
10 (0.56, 0.09)   11 (0.40, 0.07)   12 (0.17, 0.04)
```

Interior straight edges: **2–11, 3–10, 4–9, 5–8**. These produce a strip of narrow faces from hinge to tip.

## Right hindwing

This is another **separate flat island** behind the forewing. Connect vertices 1–10 in order and close 10→1. Vertices 1 and 10 extend under the thorax. Mirror for the left hindwing.

```text
 1 (0.16, -0.02)   2 (0.32,  0.00)   3 (0.49, -0.03)
 4 (0.67, -0.08)   5 (0.80, -0.14)   6 (0.83, -0.21)
 7 (0.76, -0.26)   8 (0.61, -0.27)   9 (0.42, -0.21)
10 (0.17, -0.13)
```

Interior edges: **2–9, 3–8, 4–7**.

## Finishing the small game model

- The drawing also shows three simple legs and one antenna per side. They are optional silhouette detail at this screen size; make them tapered and short if included.
- Raise and round the head, thorax, and abdomen in Z, with the thorax highest. Keep the wing surfaces flat and slightly below the body top so the thorax hides their inner roots.
- A useful first pass is body outline at `Z = 0`, dorsal centerline around `Z = +0.15` at the head, `+0.22` at the thorax, and `+0.18` at the abdomen. The wings can remain near `Z = +0.03`. These are starting proportions, not required exact heights.
- Keep body and wings as separate geometry islands even if you later join them into one Unity mesh. Do not weld the wing root vertices to the body.
- For the current wing shader, mark **all wing vertices** with UV1/WingMotion X of `1` (or `-1`); mark body and legs `0`. The shader determines left/right from mesh-local X. UV1 Y may be `0` on forewings and `1` on hindwings for a slight phase lag.
- Use texture or material color for fuzzy shading and broad abdomen bands. Adding real fur strands would add needless vertices to this distant ambient insect.
