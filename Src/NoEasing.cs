using HarmonyLib;

namespace com.github.lhervier.ksp.fix.physicseasing
{
    /// <summary>
    /// Skips every physics easing of the game: a vessel leaving the rails landed or splashed, and a vessel just docked,
    /// keep their full weight.
    ///
    /// The game eases a vessel into physics in these two cases only (GameSettings.PHYSICS_EASE, on by default):
    /// VesselPrecalculate.StartEasing sets Vessel.gravityMultiplier to 0, and it then grows by 1/16 at every physics
    /// step. Off the rails, it softens the jolts of a vessel starting badly: its wheels, built a physics step after the
    /// unpack, do not hold it for that step. It also takes the weight off a vessel at rest: its suspensions push it up,
    /// and it bounces as the weight comes back. <see cref="PhysicsHold"/> holds a vessel on wheels still until they hold
    /// it instead. After a docking, it softens the game's snap of one vessel into line with the other, which a docking
    /// fix that leaves the vessels where they stand no longer does.
    /// </summary>
    [HarmonyPatch(typeof(VesselPrecalculate), "StartEasing")]
    internal static class NoEasing
    {
        private static AccessTools.FieldRef<VesselPrecalculate, bool> easeDocking;

        /// <summary>Applies the patch. Throws when it cannot be applied: vessels are then eased as stock eases them.</summary>
        public static void Install(Harmony harmony)
        {
            easeDocking = AccessTools.FieldRefAccess<VesselPrecalculate, bool>("easeDocking");
            harmony.CreateClassProcessor(typeof(NoEasing)).Patch();
        }

        private static bool Prefix(VesselPrecalculate __instance)
        {
            // VesselPrecalculate.CalculateGravity starts the easing for a vessel leaving the rails from IDLE (landed or
            // splashed), or after a docking (easeDocking, cleared by CalculateGravity right after this call).
            Vessel vessel = __instance.Vessel;
            Log.Debug($"{(vessel != null ? vessel.GetDisplayName() : "?")}: physics easing skipped " +
                (easeDocking(__instance) ? "after a docking" : "as it leaves the rails"));
            return false;
        }
    }
}
