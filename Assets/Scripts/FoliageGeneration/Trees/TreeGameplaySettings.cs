using System;
using UnityEngine;

[Serializable]
public sealed class TreeGameplaySettings
{
    public bool enabled = true;
    [Min(.05f), Tooltip("Individual tree-origin activation distance in chunk widths, independent of render distance.")]
    public float activationRadiusChunks = .65f;
    [Min(.05f), Tooltip("Keep active proxies until this distance. Runtime enforces a gap beyond activation distance to prevent repeated toggling.")]
    public float releaseRadiusChunks = .85f;
    [Header("Canopy Queries")]
    public bool enableCanopyQueries = true;
    [Min(.05f), Tooltip("Canopy activation by tree-origin XZ distance, independent of physical trunk activation. Allow enough distance for query reach plus the largest scaled canopy radius and streaming margin.")]
    public float queryCanopyActivationRadiusChunks = .65f;
    [Min(.05f), Tooltip("Canopy release distance. Runtime enforces hysteresis above canopy activation distance.")]
    public float queryCanopyReleaseRadiusChunks = .85f;
    [Header("Scheduling and Pooling")]
    [Min(.01f), Tooltip("Seconds between nearby registry scans. Movement of one metre or registry changes also refresh the candidates.")]
    public float scanIntervalSeconds = .1f;
    [Min(1), Tooltip("Maximum proxy activations per frame, including pool reuse. Closest trees activate first.")]
    public int maxActivationsPerFrame = 8;
    [Min(0f), Tooltip("Approximate activation work budget in milliseconds. Zero uses only the count cap. At least one candidate is processed so work can progress.")]
    public float activationBudgetMs = .5f;
    [Min(0), Tooltip("Maximum inactive proxies retained across all tree templates. Active proxies are never dropped to meet this limit.")]
    public int maxPooledProxies = 128;
}
