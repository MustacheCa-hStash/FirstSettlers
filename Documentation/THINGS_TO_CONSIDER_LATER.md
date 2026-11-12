# Things to consider later

## Clustered LODs for trees and large shrubs

Use Toni Macaroni’s forest showcase as a reference for selecting detail at the branch or crown-cluster level. Start with a reduced whole-tree mesh for the low-branched oak as the baseline. Compare it against the current mesh-to-impostor transition on the same meadow walk-up, from both the lit and backlit sides.

Consider a GPU-driven cluster prototype if the intermediate mesh improves the transition and profiling still shows substantial geometry or shadow cost. Measure foliage and shadow GPU time, overdraw, and transition stability. Check that the lower crown still screens trunks and forest floor, and that wind and shadows stay coherent as detail changes.

[Toni’s showcase](https://www.youtube.com/watch?v=uR5EvBWxN14) · [Project rendering review](FOREST_RENDERING_REVIEW.md)

## Tree bark after the daylight and sky pass

Check leaf shadow reception on trunks and branches separately from ambient fill under the canopy. Improve bark texture at mid-distance and reduce grazing-angle shine, then check both changes across the mesh-to-impostor transition.
