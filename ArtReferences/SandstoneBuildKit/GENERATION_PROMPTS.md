# Sandstone catalog generation prompts

## Assembly correction — 05-homestead-assembly-v2.png

Edit target: `05-homestead-assembly.png`; built-in image generation tool. The original is retained as a sibling reference; v2 is the selected assembly.

```text
Use case: precise-object-edit. Edit this sandstone homestead concept sheet with only two localized geometry corrections. Preserve the composition, ivory paper, orange/apricot stone, roof, buildings, labels, typography, swatches, borders, all other components and lighting.
1. At the interior SS10 Partition callout, replace the small block-masonry divider with TWO broad upright cleft sandstone slabs abutting side by side, total 2 m wide and 1.50 m high, thin 0.25 m depth. Each is a single broad face with natural cleft texture and no horizontal bed joints. This is a stone slab partition, not a wall of small blocks. Preserve its existing location and leader label.
2. At the SS24 Hearth curb callout, make the curb clearly a complete square ring, low orange stone around an open earth centre, outside 1.50 x 1.50 m with a 0.25 m-wide border. Clear four square corners, no round central ring of rocks. No fire. Preserve its location and leader label.
Change nothing else.
```

Built-in image generation tool; `stylized-concept`. Prompts combine the shared direction below with the respective sheet text. Sheet 01 references `../LimestoneBuildKit/01-core-masonry.png`; sheets 02–05 reference the generated `01-core-masonry.png` as a style reference. README.md governs dimensions and fit.

## Shared direction

```text
Use case: stylized-concept.
Asset type: illustrated modular building concept catalog for the game First Settlers. Create a high resolution landscape 3:2 sheet. Match the reference's warm ivory paper, fine brown ink, painterly gouache architectural object studies, elegant dark brown serif headings, thin ruled borders, and generous readable composition. Six panels in a three-column two-row grid. Header exactly "FIRST SETTLERS / SANDSTONE". Small footer "CONCEPT CATALOG | 0.25 m GRID | 4 m BAY | 3 m STOREY".
Material: matte naturally iron-stained sandstone, recognizably ORANGE APRICOT OCHRE rather than pale yellow sandstone. Main color #C88750, sunny faces #DBA46D, light cut edges #E9BB86, occasional muted rusty blocks #B46F43, warm earth recessed joints #997452. Approximate these swatches with restrained painterly tonal variation. A little more orange than Minecraft sandstone, but not bright saturated terracotta brick. Broad readable irregular rectangular blocks in bedded courses, gentle sediment lines, granular chisel marks, softened thin edge chips. No tiny noisy speckle, glossy stone, black mortar grids, hieroglyphs, pyramids, classical columns, medieval battlements or arches. Construction language: practical Iron Age inspired rural buildings, simple slab lintels and dry-laid/bedded stone. Not a desert palace.
Three-quarter views with empty background and soft contact shadows, consistent object camera. Each component has its exact SS## identifier, short part name and supplied dimensions in metres. Written specification governs painted proportions. No extra numbered pieces, no duplicate IDs. Avoid excessive annotations; preserve large legible artwork. Reference image is a STYLE AND LAYOUT reference only: replace its pale limestone with our orange sandstone and our specified components.
```

## 01-core-masonry.png

```text
Sheet heading "01 / CORE MASONRY".
Top left SS01 "BEDDED SANDSTONE WALL": 4 W x 3 H x 0.50 D m. Rough-squared orange stone with elongated blocks and subtly stratified horizontal beds.
Top middle SS02 "DRESSED SANDSTONE WALL": 4 W x 3 H x 0.50 D m. More regular squared orange blocks, shallow broad tooling, calmer seams.
Top right SS03 "NARROW WALL": 2 W x 3 H x 0.50 D m. Half the span, SAME block size and storey height as SS02.
Bottom left SS04 "SQUARE PIER": 0.50 W x 3 H x 0.50 D m. Plain stacked squared orange sandstone; no ornate capital.
Bottom middle SS05 "FLAGSTONE PAVING": 4 x 4 m footprint, 0.25 m deep. Large irregular rectangular apricot-orange slabs; softly cleft tops on ground bedding.
Bottom right SS06 "STRIP FOOTING": 4 L x 0.50 H x 1 D m. Broad low rough-bedded orange masonry strip with flat top. Caption "Bedded below wall".
```

## 02-openings-and-interiors.png

```text
Sheet heading "02 / OPENINGS AND INTERIORS".
Top left SS07 "LINTEL DOORWAY": full wall 4 W x 3 H x 0.50 D m. Empty square doorway centred in orange masonry, clear 1.25 W x 2.25 H m. A SINGLE thick sandstone lintel rests across it with real seats; no row of small stones pretending to span it, no arch, no door leaf.
Top middle SS08 "SPLAYED WINDOW": full wall 4 W x 3 H x 0.50 D m. Small unglazed rectangular window, outside aperture 0.50 W x 1 H m, sill 1.25 m, opening widens inward to 1 W m. Show an interior three-quarter view and small plan inset to reveal the flaring jambs. Straight lintel.
Top right SS09 "STORAGE NICHE WALL": full wall 4 W x 3 H x 0.50 D m. ONE modest rectangular interior recess 1 W x 0.75 H x 0.25 deep m, sill 1 m; SOLID rear sandstone backing 0.25 thick, lintel over pocket. This is not a through window. Small unnumbered section inset makes the backing clear. No pots required.
Bottom left SS10 "UPRIGHT SLAB PARTITION": 2 W x 1.50 H x 0.25 D m. Two broad vertical orange sandstone slabs abutting as one low internal divider. Cleft stone faces, squared bottom on bedding, no fence posts or cap.
Bottom middle SS11 "HALF-GABLE": 2 base x 2 rise x 0.50 D m. One RIGHT TRIANGLE of bedded orange masonry, horizontal base, one vertical end, one 45 degree diagonal. NOT a full symmetrical triangle, no rectangular wall below. Small unnumbered outline shows two mirrored halves forming a 4 m span, 2 m rise full gable.
Bottom right SS12 "BEAM-SEAT WALL": 4 W x 3 H x 0.50 D m. Orange full-height wall with ONE shallow rectangular notch centered in its top edge: 0.50 W x 0.25 H m, passing across the full 0.50 thickness. A dotted simple timber beam outline shows where a separate 0.50-wide x 0.25-deep timber seats at the 3 m top datum. Caption "Timber separate". NO castellated top, no repeated crenels.
```

## 03-court-and-household.png

```text
Sheet heading "03 / COURT AND HOUSEHOLD".
Top left SS13 "LOW COURT WALL": 4 W x 1.25 H x 0.50 D m. Low uncapped wall of orange bedded rectangular blocks.
Top middle SS14 "UPRIGHT SLAB FENCE": 2 W x 1.25 H x 0.25 D m exposed. Several tall abutting naturally cleft sandstone slabs embedded in soil; narrow grounded boundary, irregular but restrained skyline. Small dashed line shows 0.25 m embedded below ground.
Top right SS15 "ALTERNATING COPING": 4 L x 0.25 H x 0.50 D m. Standalone long cap assembly of alternating upright taller and flatter short sandstone capstones, ALL inside the 0.25 high envelope. A genuinely rough stone cap rather than ornament. Tiny unnumbered wall outline below. Caption "SS13 + SS15 = 1.50 m".
Bottom left SS16 "COURT GATEPOST": 0.75 W x 1.50 H x 0.75 D m. Broad square bedded sandstone post, plain weather-shedding stone cap INCLUDED in total size. No gate, no finial.
Bottom middle SS17 "SLAB BENCH": 2 L x 0.50 H x 0.50 D m. Thick apricot sandstone seat slab on TWO low solid stone feet; open clear space between feet. Simple backless seat.
Bottom right SS18 "CARVED TROUGH": 2 L x 0.50 H x 0.75 D m. Low rectangular hollow orange stone trough, solid base, thick walls, visibly open interior. One rounded shallow inner basin; no water, no plumbing, no decoration.
```

## 04-access-and-working-stone.png

```text
Sheet heading "04 / ACCESS AND WORKING STONE".
Top left SS19 "THRESHOLD STEPS": 1.50 W m, 0.50 rise m, 1 run m. Exactly two broad 0.25 m risers and 0.50 m goings, solid orange masonry mass beneath. No rails.
Top middle SS20 "HALF-STOREY STAIR": 1.50 W m, 1.50 rise m, 2 run m. Eight orange broad stone steps of 0.1875 m rise, 0.25 going; entirely SOLID filled masonry beneath flight, no timber stringers and no floating steps. No handrail.
Top right SS21 "BATTERED TERRACE WALL": 4 W x 1.50 H x 0.75 D m. Retaining mass, broad flat base 0.75 deep, flat vertical back, exposed orange bedded stone front leans inward, top 0.50 deep. No implied soil mechanics. Small end section.
Bottom left SS22 "OPEN DRAIN CHANNEL": 2 L x 0.25 H x 0.50 D m. Separate low carved orange sandstone channel unit, open shallow U-shaped trough through its full length, flat external bottom, clearly visible inlet and outlet ends. Width of inside water channel 0.25 m. No water, grating, pipe or outlets through walls.
Bottom middle SS23 "CORBEL SHELF": 1 W x 0.50 H x 0.50 projection m overall. Thick broad stone shelf rests on TWO short stepped stone corbels. Ghost wall in fine dotted outline; concealed rear tails shown in a small section, caption "Built into masonry". No freestanding legs, no ornate scroll brackets, no timber.
Bottom right SS24 "HEARTH CURB": 1.50 x 1.50 m outside footprint, 0.25 m tall. Simple SQUARE perimeter of low orange sandstone curb stones enclosing a packed-earth centre. Visible hollow centre; no floor slab, no fire, no chimney, no oven, no magical glow.
```

## 05-homestead-assembly.png

```text
Use a different layout for the assembly sheet: large hero cutaway on left two thirds, two small inset studies on right, matching ivory paper and brown serif captions, not a six-part grid. Sheet heading "05 / SANDSTONE HOMESTEAD". This is a painterly assembly CONCEPT, not a measured reconstruction.
Hero: a modest one-storey rural homestead/workshop, 4 x 8 m nominal wall-centreline plan, 3 m eaves, 5 m ridge. Orange apricot bedded sandstone walls SS01, dressed corner details SS02, square slab-lintel doorway SS07, small splayed rectangular window SS08. Timber roof structure and golden reed thatch, masonry triangular gable from mirrored SS11 halves, NO chimney, no battlements or arches. A front roof cutaway reveals simple wood rafters and one interior blind storage niche SS09 with solid backing, low slab partition SS10, and stone hearth curb SS24 on earth inset. Orange flagstone SS05 on ground, threshold steps SS19 reaching a 0.50 m raised house platform. Roof timber at 3 m datum; substantial eaves protecting sandstone.
Top right inset "WORKING COURT": low orange wall SS13 with alternating stone coping SS15, upright-slab boundary SS14 along one side, two short gateposts SS16 framing open gap. Backless orange stone slab bench SS17 and carved trough SS18 on court ground. Some muted olive plants and earth, no desert dunes, exotic palace or people.
Bottom right inset "TERRACE AND DRAIN": orange battered retaining wall SS21 edges a modest raised earth terrace, shallow open channel SS22 along base leads toward an open gravel soakaway. No working water simulation implied.
Use only a few clear leader labels with IDs near respective pieces; not every object needs a label. Include five small color swatches labelled "APRICOT", "OCHRE", "CUT EDGE", "RUST", "JOINT". Orange sandstone must be clearly visually distinct from yellow thatch and brown wood. Lighting neutral soft daylight, colour belongs to stone rather than sunset tint. Avoid saturated red brick, all-yellow cream stone, palaces, domes, pyramids, carved symbols, glazed windows and medieval chimneys.
```

