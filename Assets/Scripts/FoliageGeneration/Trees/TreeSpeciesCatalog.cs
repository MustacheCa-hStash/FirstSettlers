using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One binding table for authored tree variants, habitat fallbacks, identity and datacards.</summary>
public static class TreeSpeciesCatalog
{
    public sealed class Species
    {
        public WorldFeatureVariant Variant { get; }
        public string DisplayName { get; }
        public bool IsGrassland { get; }
        private readonly Func<TreeSettings, GameObject> near, billboard;
        private readonly Func<TreeSettings, WorldObjectDefinition> definition;
        internal Species(WorldFeatureVariant variant, string name, bool grassland,
            Func<TreeSettings, GameObject> near, Func<TreeSettings, GameObject> billboard,
            Func<TreeSettings, WorldObjectDefinition> definition)
        { Variant = variant; DisplayName = name; IsGrassland = grassland; this.near = near; this.billboard = billboard; this.definition = definition; }
        public GameObject NearPrefab(TreeSettings settings)
        {
            if (settings == null) return null;
            var prefab = near(settings);
            return prefab != null ? prefab : IsGrassland && settings.grasslandTreeFallbackPrefab != null
                ? settings.grasslandTreeFallbackPrefab : settings.treeLOD0GameObjectPrefab;
        }
        public GameObject BillboardPrefab(TreeSettings settings)
        {
            if (settings == null) return null;
            var prefab = billboard(settings);
            return prefab != null ? prefab : IsGrassland && settings.grasslandTreeBillboardFallbackPrefab != null
                ? settings.grasslandTreeBillboardFallbackPrefab : settings.treeBillboardPrefab;
        }
        public WorldObjectDefinition Definition(TreeSettings settings)
        {
            if (settings == null) return null;
            var card = definition(settings);
            if (card != null) return card;
            var prefab = NearPrefab(settings);
            return prefab != null && prefab.TryGetComponent<TreeGameplayAuthoring>(out var authoring) ? authoring.QueryDefinition : null;
        }
    }
    private static readonly Species[] entries =
    {
        new(WorldFeatureVariant.MapleTree, "Maple Tree", false, s=>s.mapleTreePrefab, s=>s.mapleTreeBillboardPrefab, s=>s.mapleTreeDefinition),
        new(WorldFeatureVariant.SugarMapleTree, "Sugar Maple Tree", false, s=>s.sugarMapleTreePrefab, s=>s.sugarMapleTreeBillboardPrefab, s=>s.sugarMapleTreeDefinition),
        new(WorldFeatureVariant.BirchAspenTree, "Birch / Aspen Tree", false, s=>s.birchAspenTreePrefab, s=>s.birchAspenTreeBillboardPrefab, s=>s.birchAspenTreeDefinition),
        new(WorldFeatureVariant.BeechTree, "Beech Tree", false, s=>s.beechTreePrefab, s=>s.beechTreeBillboardPrefab, s=>s.beechTreeDefinition),
        new(WorldFeatureVariant.SpruceTree, "Spruce Tree", false, s=>s.spruceTreePrefab, s=>s.spruceTreeBillboardPrefab, s=>s.spruceTreeDefinition),
        new(WorldFeatureVariant.WhitePineTree, "White Pine Tree", false, s=>s.whitePineTreePrefab, s=>s.whitePineTreeBillboardPrefab, s=>s.whitePineTreeDefinition),
        new(WorldFeatureVariant.OakTree, "Oak Tree", false, s=>s.oakTreePrefab, s=>s.oakTreeBillboardPrefab, s=>s.oakTreeDefinition),
        new(WorldFeatureVariant.GrasslandMapleTree, "Maple Tree", true, s=>s.grasslandMapleTreePrefab, s=>s.grasslandMapleTreeBillboardPrefab, s=>s.mapleTreeDefinition),
        new(WorldFeatureVariant.GrasslandBirchAspenTree, "Birch / Aspen Tree", true, s=>s.grasslandBirchAspenTreePrefab, s=>s.grasslandBirchAspenTreeBillboardPrefab, s=>s.birchAspenTreeDefinition),
        new(WorldFeatureVariant.GrasslandWhitePineTree, "White Pine Tree", true, s=>s.grasslandWhitePineTreePrefab, s=>s.grasslandWhitePineTreeBillboardPrefab, s=>s.whitePineTreeDefinition),
        new(WorldFeatureVariant.GrasslandOakTree, "Oak Tree", true, s=>s.grasslandOakTreePrefab, s=>s.grasslandOakTreeBillboardPrefab, s=>s.oakTreeDefinition),
        new(WorldFeatureVariant.GrasslandWillowTree, "Willow Tree", true, s=>s.grasslandWillowTreePrefab, s=>s.grasslandWillowTreeBillboardPrefab, s=>s.willowTreeDefinition)
    };
    public static IReadOnlyList<Species> All { get; } = Array.AsReadOnly(entries);
    private static readonly Species[] byVariant = BuildIndex();
    private static Species[] BuildIndex()
    {
        int size = 0;
        foreach (var entry in entries) size = Math.Max(size, (int)entry.Variant + 1);
        var result = new Species[size];
        foreach (var entry in entries)
        {
            int index = (int)entry.Variant;
            if (result[index] != null) throw new InvalidOperationException("Duplicate tree variant: " + entry.Variant);
            result[index] = entry;
        }
        return result;
    }
    public static bool TryGet(WorldFeatureVariant variant, out Species species)
    {
        int index = (int)variant;
        species = (uint)index < (uint)byVariant.Length ? byVariant[index] : null;
        return species != null;
    }
    public static bool IsGrassland(WorldFeatureVariant variant) => TryGet(variant, out var species) && species.IsGrassland;
    public static string DisplayName(WorldFeatureVariant variant) => TryGet(variant, out var species) ? species.DisplayName : "Tree";
}
