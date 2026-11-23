using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.physicseasing
{
    /// <summary>
    /// Reads the settings and installs the patches, once, when KSP starts: from then on, a landed or splashed vessel
    /// leaving the rails starts with its full weight, held still until its wheels hold it.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPFixPhysicsEasing : MonoBehaviour
    {
        private const string HarmonyId = "com.github.lhervier.ksp.fix.physicseasing";

        private void Start()
        {
            Settings.Load();
            string version = typeof(KSPFixPhysicsEasing).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Log.Info($"Version {version}, log level {Log.Level}");

            Harmony harmony = new Harmony(HarmonyId);
            // The easing is skipped only once the hold that replaces it is in place: without the hold, a vessel
            // standing on its wheels would fall freely for the step before its wheels touch the ground.
            try
            {
                WheelSetupPatch.Install(harmony);
                HeldVesselOrbit.Install(harmony);
                PhysicsHold.CanHold = true;
                Log.Info("Landed and splashed vessels held still until their wheels hold them");
                NoEasingOffRails.Install(harmony);
                Log.Info("Physics easing skipped for vessels leaving the rails landed or splashed");
            }
            catch (Exception e)
            {
                Log.Error("Could not hold vessels or skip the physics easing, landed and splashed vessels are " +
                    $"{(PhysicsHold.CanHold ? "held but " : "")}still eased into physics: {e}");
            }
        }
    }
}
