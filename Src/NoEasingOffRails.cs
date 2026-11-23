using HarmonyLib;

namespace com.github.lhervier.ksp.fix.physicseasing
{
    /// <summary>
    /// Skips the physics easing of every vessel leaving the rails landed or splashed: it starts with its full weight.
    /// The easing that follows a docking is left to the game.
    ///
    /// When a landed or splashed vessel leaves the rails, KSP eases it into physics (GameSettings.PHYSICS_EASE, on by
    /// default): VesselPrecalculate.StartEasing sets Vessel.gravityMultiplier to 0, and it then grows by 1/16 at every
    /// physics step. It softens the jolts of a vessel starting badly: its wheels, built a physics step after the
    /// unpack, do not hold it for that step. It also takes the weight off a vessel at rest: its suspensions push it
    /// up, and it bounces as the weight comes back. <see cref="PhysicsHold"/> holds a vessel on wheels still until they
    /// hold it instead.
    /// </summary>
    [HarmonyPatch(typeof(VesselPrecalculate), "StartEasing")]
    internal static class NoEasingOffRails
    {
        private static AccessTools.FieldRef<VesselPrecalculate, bool> easeDocking;

        /// <summary>Applies the patch. Throws when it cannot be applied: vessels are then eased as stock eases them.</summary>
        public static void Install(Harmony harmony)
        {
            easeDocking = AccessTools.FieldRefAccess<VesselPrecalculate, bool>("easeDocking");
            harmony.CreateClassProcessor(typeof(NoEasingOffRails)).Patch();
        }

        private static bool Prefix(VesselPrecalculate __instance)
        {
            // VesselPrecalculate.CalculateGravity starts the easing for a vessel leaving the rails from IDLE (landed or
            // splashed), or after a docking (easeDocking).
            if (easeDocking(__instance))
            {
                return true;
            }
            Vessel vessel = __instance.Vessel;
            Log.Debug($"{(vessel != null ? vessel.GetDisplayName() : "?")}: physics easing skipped as it leaves the rails");
            return false;
        }
    }
}
