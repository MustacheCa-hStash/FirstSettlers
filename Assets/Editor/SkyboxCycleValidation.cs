using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SkyboxCycleValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Time/Validate Skybox Cycle")]
    public static void Run()
    {
        Material original = RenderSettings.skybox;
        FieldInfo singleton = typeof(GameTimeManager).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previousClock = singleton.GetValue(null);
        var go = new GameObject("Skybox cycle validation") { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false);
        Material day = null, twilight = null, night = null, external = null;
        SkyboxCycleController controller = null;
        try
        {
            singleton.SetValue(null, null);
            Shader shader = Shader.Find("Skybox/Procedural");
            Require(shader != null, "Validation skybox shader unavailable.");
            day = new Material(shader); twilight = new Material(shader);
            night = new Material(shader); external = new Material(shader);
            var clock = go.AddComponent<GameTimeManager>();
            Set(clock, "startAutomatically", false);
            Invoke(clock, "Awake");
            controller = go.AddComponent<SkyboxCycleController>();
            Set(controller, "timeManager", clock);
            Set(controller, "daySkybox", day);
            Set(controller, "sunriseSunsetSkybox", twilight);
            Set(controller, "nightSkybox", night);
            Set(controller, "updateDynamicGIOnSwap", false);
            Invoke(controller, "OnEnable");
            clock.SetTime(0, 23, 30);
            Invoke(controller, "Start");
            Require(RenderSettings.skybox == night, "Startup did not apply nighttime skybox.");

            Check(clock, controller, 4, 59, night, SkyboxCycleController.SkyPhase.Night);
            Check(clock, controller, 5, 0, twilight, SkyboxCycleController.SkyPhase.SunriseSunset);
            Check(clock, controller, 6, 0, twilight, SkyboxCycleController.SkyPhase.SunriseSunset);
            Check(clock, controller, 6, 59, twilight, SkyboxCycleController.SkyPhase.SunriseSunset);
            Check(clock, controller, 7, 0, day, SkyboxCycleController.SkyPhase.Day);
            Check(clock, controller, 20, 59, day, SkyboxCycleController.SkyPhase.Day);
            Check(clock, controller, 21, 0, twilight, SkyboxCycleController.SkyPhase.SunriseSunset);
            Check(clock, controller, 22, 0, twilight, SkyboxCycleController.SkyPhase.SunriseSunset);
            Check(clock, controller, 22, 59, twilight, SkyboxCycleController.SkyPhase.SunriseSunset);
            Check(clock, controller, 23, 0, night, SkyboxCycleController.SkyPhase.Night);
            clock.AdvanceGameMinutes(6 * 60);
            Require(RenderSettings.skybox == twilight, "Advancing across midnight missed sunrise.");
            clock.AdvanceGameMinutes(-61);
            Require(RenderSettings.skybox == night, "Backward jump missed nighttime.");
            clock.SetTime(0, 0, 0);
            clock.AdvanceGameMinutes(-17 * 60);
            Require(RenderSettings.skybox == day, "Negative day wrapping failed.");

            Set(controller, "twilightHours", 0f);
            Check(clock, controller, 6, 0, day, SkyboxCycleController.SkyPhase.Day);
            Check(clock, controller, 22, 0, night, SkyboxCycleController.SkyPhase.Night);
            Set(controller, "twilightHours", 0.5f);
            Check(clock, controller, 5, 29, night, SkyboxCycleController.SkyPhase.Night);
            Check(clock, controller, 5, 30, twilight, SkyboxCycleController.SkyPhase.SunriseSunset);
            Check(clock, controller, 6, 30, day, SkyboxCycleController.SkyPhase.Day);

            // Shift dawn to 20:00 so daylight crosses midnight.
            Set(clock, "daylightStartHour", 20);
            Set(controller, "twilightHours", 1f);
            Check(clock, controller, 0, 0, day, SkyboxCycleController.SkyPhase.Day);
            Check(clock, controller, 11, 0, twilight, SkyboxCycleController.SkyPhase.SunriseSunset);
            Check(clock, controller, 13, 0, night, SkyboxCycleController.SkyPhase.Night);
            Check(clock, controller, 19, 0, twilight, SkyboxCycleController.SkyPhase.SunriseSunset);

            // A ten-hour day with a shorter night must use this clock's own period.
            Set(clock, "daylightHours", 6); Set(clock, "nighttimeHours", 4);
            Set(clock, "daylightStartHour", 2);
            Check(clock, controller, 1, 0, twilight, SkyboxCycleController.SkyPhase.SunriseSunset);
            Check(clock, controller, 3, 0, day, SkyboxCycleController.SkyPhase.Day);
            Check(clock, controller, 7, 0, twilight, SkyboxCycleController.SkyPhase.SunriseSunset);
            Check(clock, controller, 9, 0, night, SkyboxCycleController.SkyPhase.Night);
            Set(controller, "twilightHours", 100f);
            Check(clock, controller, 4, 0, day, SkyboxCycleController.SkyPhase.Day);

            Set(controller, "twilightHours", 1f);
            Set(controller, "sunriseSunsetSkybox", null);
            Check(clock, controller, 2, 0, day, SkyboxCycleController.SkyPhase.SunriseSunset);
            Set(controller, "nightSkybox", null);
            Check(clock, controller, 9, 0, day, SkyboxCycleController.SkyPhase.Night);
            Set(controller, "daySkybox", null);
            Invoke(controller, "LateUpdate");
            Require(RenderSettings.skybox == original, "Empty slots did not restore scene fallback while paused.");

            Set(controller, "daySkybox", day);
            Invoke(controller, "LateUpdate");
            Require(RenderSettings.skybox == day, "Paused Inspector material edits were ignored.");
            Invoke(controller, "OnDisable");
            Require(RenderSettings.skybox == original, "Disable did not restore scene skybox.");
            clock.SetTime(0, 3, 0);
            Require(RenderSettings.skybox == original, "Disabled controller remains subscribed.");
            Invoke(controller, "OnEnable");
            Invoke(controller, "LateUpdate");
            Require(RenderSettings.skybox == day, "Re-enable did not resume swapping.");
            RenderSettings.skybox = external;
            Invoke(controller, "OnDisable");
            Require(RenderSettings.skybox == external, "Disable overwrote another skybox owner's change.");

            Debug.Log("SKYBOX CYCLE PASS: startup, dawn/dusk boundaries, paused SetTime, forward/backward and negative-day jumps, midnight wrap, shifted dawn, non-24-hour cycle, zero/fractional/oversized twilight, missing materials, paused edits, disable/re-enable and unsubscription.");
        }
        finally
        {
            if (controller != null) Invoke(controller, "OnDisable");
            Object.DestroyImmediate(go);
            foreach (Material material in new[] { day, twilight, night, external })
                if (material != null) Object.DestroyImmediate(material);
            RenderSettings.skybox = original;
            singleton.SetValue(null, previousClock);
        }
    }

    private static void Check(GameTimeManager clock, SkyboxCycleController controller, int hour, int minute,
        Material expected, SkyboxCycleController.SkyPhase phase)
    {
        clock.SetTime(0, hour, minute);
        Require(RenderSettings.skybox == expected && controller.CurrentPhase == phase,
            $"Wrong skybox/phase at {hour:00}:{minute:00}: {controller.CurrentPhase}.");
    }

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, PrivateInstance).SetValue(target, value);

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, PrivateInstance).Invoke(target, null);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
