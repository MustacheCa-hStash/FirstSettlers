# Time-of-day skyboxes

In **SmearScene > WorldSystems > GameTimeManager > Skybox Cycle Controller**, assign:

- **Day Skybox**: daytime material (default: `CasualDay`).
- **Sunrise Sunset Skybox**: shared dawn/dusk material (default: `CloudedSunGlow`).
- **Night Skybox**: nighttime material (default: `CoriolisNight4k`).

**Twilight Hours** is the number of game hours on each side of dawn and dusk. The controller follows **GameTimeManager > Daylight Start Hour**, **Daylight Hours**, and **Nighttime Hours**, including cycles that cross midnight and non-24-hour days. With the current 06:00 dawn, 22:00 dusk and a width of 1:

| Game time | Skybox |
| --- | --- |
| 05:00–07:00 | Sunrise/sunset |
| 07:00–21:00 | Day |
| 21:00–23:00 | Sunrise/sunset |
| 23:00–05:00 | Night |

Intervals include the start and exclude the end. Zero disables twilight. Oversized widths are limited to half the shorter day/night phase to keep the windows from overlapping.

Swaps apply at startup and when time advances or is set directly, including while paused. Inspector changes are picked up each frame in Play mode. This is a direct material swap; it does not crossfade textures. Shared material assets are never modified. An empty twilight/night slot uses the day material; an empty day slot uses the scene's original skybox. Disabling the controller restores that original skybox if this controller still owns the current assignment.

**Update Dynamic GI On Swap** optionally refreshes the sky environment when the material changes; baked reflection probes are not rebaked. Existing sun, moon, ambient lighting and fog remain controlled by `SunCycleController`. Cameras must use a skybox background and must not override the scene skybox with a Camera Skybox component.

For other scenes, add `SkyboxCycleController` to the clock object (or assign its clock reference) and assign the materials. Only one active skybox controller should manage a scene's skybox.

Validation menu: **Tools > Time > Validate Skybox Cycle**.
