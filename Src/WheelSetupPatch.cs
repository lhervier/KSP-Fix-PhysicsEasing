using HarmonyLib;
using ModuleWheels;

namespace com.github.lhervier.ksp.fix.physicseasing
{
    /// <summary>
    /// As soon as the game builds a wheel, keeps its part held still while its vessel is.
    ///
    /// ModuleWheelBase builds a wheel (wheelSetup) by a coroutine that waits for its part to be unpacked: the frame
    /// after the unpack, once a physics step has gone by with no wheel at all. Building it makes its part's body
    /// dynamic again (<see cref="PhysicsHold.OnWheelBuilt"/>).
    /// </summary>
    [HarmonyPatch(typeof(ModuleWheelBase), "wheelSetup")]
    internal static class WheelSetupPatch
    {
        /// <summary>Applies the patch. Throws when it cannot be applied.</summary>
        public static void Install(Harmony harmony)
        {
            harmony.CreateClassProcessor(typeof(WheelSetupPatch)).Patch();
        }

        private static void Postfix(ModuleWheelBase __instance)
        {
            Part part = __instance.part;
            Vessel vessel = part != null ? part.vessel : null;
            PhysicsHold hold = vessel != null ? vessel.GetComponent<PhysicsHold>() : null;
            if (hold != null)
            {
                hold.OnWheelBuilt(part);
            }
        }
    }
}
