# Live tree season preview

Select World Manager and open **Tree Season Preview**. Enable **Simulate Tree Season**, then move **Summer to Autumn** from `0` (summer) to `1` (autumn). The **Summer** and **Autumn** buttons enable the preview and select the corresponding endpoint. Intermediate slider values blend the palettes.

The preview updates existing cached/instanced leaf materials immediately, in Play mode and through the World Manager Inspector in Scene view. No regeneration is needed. It does not change tree placement, geometry, colliders, leaf alpha masks or source material settings. Turning the toggle off, disabling the owning World Manager, or destroying it releases the override and restores authored material behavior.

Supported shaders include the new sugar/red maple leaf shaders, legacy sugar/red maple, birch and oak leaves, and their supported tintable/semantic billboard shaders. Evergreen shaders remain unchanged. A fully baked image or an evergreen fallback impostor without a seasonal leaf palette does not acquire one merely from this control; the current scene uses a spruce fallback for unassigned species billboards.

The implementation sets `_TreeSeasonSimulationEnabled` and `_TreeSeasonSimulationAutumnAmount`, read by `TreeSeasonSimulation.hlsl`. Globals deliberately bypass per-material `_SeasonAutumnAmount` values, so runtime material clones respond without editing assets. Normal behavior is preserved when the override is disabled. `WorldManager.SetTreeSeasonSimulation(bool, float)` provides the same operation for tools, and clamps the amount to `[0,1]`.

World Manager Inspector dependency validation covers the toggle/slider. Maple GPU validation covers summer, midpoint, autumn, cached material copies, unchanged opacity, clamping and override cleanup. It also compiles the modified legacy leaf/billboard shaders.
