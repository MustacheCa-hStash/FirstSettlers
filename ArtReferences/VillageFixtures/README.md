# Village Fixtures & Everyday Life — V01–V30

Five concept sheets for First Settlers, continuing the existing ivory-paper, ink and gouache catalogs. Generated with the built-in image_gen tool on October 22, 2026.

- [01 / MARKET AND EXCHANGE](01-market-and-exchange.png)
- [02 / CONTAINERS AND CARGO](02-containers-and-cargo.png)
- [03 / WATER AND COMMUNAL CHORES](03-water-and-communal-chores.png)
- [04 / WORKYARDS AND SUPPLIES](04-workyards-and-supplies.png)
- [05 / DOMESTIC EXTERIOR AND DECORATION](05-domestic-exterior-and-decoration.png)
- [Exact generation prompts](GENERATION_PROMPTS.md)

## Catalog

| ID | Design |
| --- | --- |
| V01 | Freestanding stall frame |
| V02 | Cloth canopy, with reed-cover alternative |
| V03 | Market counter |
| V04 | Trestle display board |
| V05 | Removable produce display |
| V06 | Hanging trade marker |
| V07 | Large wooden barrel |
| V08 | Small keg |
| V09 | Open stave tub |
| V10 | Slatted transport crate |
| V11 | Lidded cargo box |
| V12 | Tied sack |
| V13 | Well surround and lifting frame |
| V14 | Water trough |
| V15 | Wooden bucket |
| V16 | Washing basin |
| V17 | Carrying yoke |
| V18 | Large water vat |
| V19 | Stacked firewood |
| V20 | Timber storage rack |
| V21 | Drying rack |
| V22 | Chopping block |
| V23 | Small handcart |
| V24 | Reed/hay bundles |
| V25 | Laundry line |
| V26 | Woven privacy screen |
| V27 | Herb planter |
| V28 | Broom/tool grouping |
| V29 | Rope/net bundle |
| V30 | Carved waypost |

## Authoring intent

These are visual concepts, not modeled or installed building pieces. Dimensions, pivots, interaction positions, collision, support, costs and recipes remain to be authored. The style is a rustic game interpretation continuing the existing catalogs, not a claim of exact historical reconstruction.

Keep V01 frame, V02 covering, V03 counter and V05 display independently reusable. V02's reed inset is an alternative covering, not an extra numbered item. Reuse F-family pottery, baskets, tables and benches when dressing stalls. Keep removable lids and optional contents separate from container bodies. Decorative rope, cloth, net and foliage should retain readable broad silhouettes at gameplay distance.

V13 illustrates a lifting mechanism and bucket; reuse the V15 bucket asset where practical. Functional water access, water supplies, lifting animation and terrain/well excavation are separate implementation work. V19/V24/V28/V29 are grouped dressing concepts; resource quantities and item pickup behavior are not established by their pictured contents.

## Proposed simulation ownership

PCG should produce initial entity records and village membership. A runtime world/entity store should own current facility state; village indexes should reference these records by stable ID. Runtime construction, removal, movement and damage must use the same update path as generated content. A loaded GameObject is an optional representation bound to the record.

Facilities should expose capabilities such as DrawWater, StoreWater, SellGoods or DryGoods, plus interaction positions, access rules, capacity, operational state and reservations. Store references to supporting build pieces where a facility depends on a composite assembly. Presence in a village index must not imply usability.

For small villages, begin with a stable-ID dictionary and short per-capability candidate lists. Add spatial indexing only when measured scale warrants it. Known-ID lookup, capability selection and navigation/reachability validation are different operations.

Destruction must explicitly update logical state, invalidate reservations, persist the change and notify affected activities before removing/recycling the representation. Streaming unload only detaches the representation. Do not equate OnDisable or OnDestroy with gameplay destruction: deactivation, pooling and scene unloading also affect Unity object lifetimes.

A routine requests a capability, filters compatible operational facilities, scores a short list by travel/access/queue, reserves an interaction slot and revalidates on arrival and before resource transfer. Reservations need cleanup on cancellation, path failure and actor removal; expiring leases can provide recovery. Preserve failed/unavailable decisions until relevant state changes or bounded retry time, rather than querying every frame.

Generated baselines plus saved changes must preserve removed entities on reload. Version generated records so future template changes can migrate saves deliberately. Distant villagers can advance logical activities without GameObjects; switching to detailed simulation must continue the same activity and reservation, without granting duplicate outputs.

## Review

All five sheets were visually reviewed for V01–V30 labels, six-item grouping, material consistency and distinct silhouettes. Final PNG copies were hash-checked against generated originals. No runtime code, scenes or existing assets were changed. No Unity tests apply to these concept-only additions.

