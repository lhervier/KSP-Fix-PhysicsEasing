using HarmonyLib;

namespace com.github.lhervier.ksp.fix.physicseasing
{
    /// <summary>
    /// Gives the orbit of a vessel <see cref="PhysicsHold"/> holds still the velocity of a vessel at rest on the
    /// surface, as the vessel is.
    ///
    /// OrbitDriver.TrackRigidbody takes the velocity of a vessel from its rigidbodies only when its root part's body is
    /// not kinematic; held, it keeps the velocity of the last call, which that call left in other axes (Swizzle). The
    /// vessel's surface speed then reads hundreds of metres per second at one step out of two: a vessel waiting on the
    /// launch pad or the runway (PRELAUNCH) faster than 2.5 m/s is taken as launched (Vessel.updateSituation), and its
    /// vertical speed gauge jumps. The physics easing hid it: the game does not test that speed while easing.
    /// </summary>
    [HarmonyPatch(typeof(OrbitDriver), "TrackRigidbody")]
    internal static class HeldVesselOrbit
    {
        /// <summary>Applies the patch. Throws when it cannot be applied.</summary>
        public static void Install(Harmony harmony)
        {
            harmony.CreateClassProcessor(typeof(HeldVesselOrbit)).Patch();
        }

        private static void Prefix(OrbitDriver __instance)
        {
            Vessel vessel = __instance.vessel;
            PhysicsHold hold = vessel != null ? vessel.GetComponent<PhysicsHold>() : null;
            if (hold == null || !hold.IsHeld)
            {
                return;
            }
            // At rest in the frame that turns with the body: the velocity TrackRigidbody gives an unheld vessel whose
            // bodies do not move.
            CelestialBody body = __instance.referenceBody;
            __instance.vel = __instance.orbit.GetRotFrameVelAtPos(body, (vessel.CoMD - body.position).xzy);
        }
    }
}
