using System.Collections.Generic;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.physicseasing
{
    /// <summary>
    /// Holds a landed or splashed vessel with wheels still as it leaves the rails, until its wheels hold it, then lets
    /// it go at rest. While the active vessel is held, its controls are locked, as the game locks them while it eases a
    /// vessel in: a command given then would be lost on bodies that cannot move.
    ///
    /// Without the physics easing (<see cref="NoEasing"/>), a vessel starts with its full weight. The game
    /// builds its wheels the frame after the unpack, and a wheel finds the ground only after a physics step of its own:
    /// until then, it pushes on nothing, and a vessel standing on its wheels falls freely. A tyre then brings its force
    /// from nothing to what holds the vessel over several steps (VehicleBase.ApplyTireForce smooths it): on a slope, a
    /// vessel let go before starts sliding, and a sliding tyre grips less than a still one.
    /// </summary>
    public class PhysicsHold : VesselModule
    {
        // How many physics steps at most a vessel is held still for its wheels to find the ground and their tyres to
        // push as they will once let go: the slowest stock tyre (tireForceSharpness 1) needs about 2 s.
        private const int MaxHoldSteps = 150;

        // How close, as a share of its force, a tyre's force must be to the one it is heading for before the vessel is
        // let go; and the force under which a tyre counts as settled whatever its share, in kN.
        private const float TyreTolerance = 0.1f;
        private const float TyreForceFloor = 0.001f;

        /// <summary>
        /// Whether vessels may be held still as they leave the rails: set once the patches the hold needs are applied
        /// (<see cref="WheelSetupPatch"/>, <see cref="HeldVesselOrbit"/>).
        /// </summary>
        internal static bool CanHold;

        /// <summary>
        /// Fired with a vessel as it is held still, and as it is let go. Other mods find them by name
        /// (GameEvents.FindEvent&lt;EventData&lt;Vessel&gt;&gt;("onPhysicsEasingHold") and "onPhysicsEasingLetGo"), without
        /// a reference to this assembly.
        /// </summary>
        public static readonly EventData<Vessel> OnHold = new EventData<Vessel>("onPhysicsEasingHold");
        public static readonly EventData<Vessel> OnLetGo = new EventData<Vessel>("onPhysicsEasingLetGo");

        /// <summary>The bodies held kinematic since the vessel left the rails; null when none is held.</summary>
        private List<Rigidbody> held;

        /// <summary>Every part of the held vessel with a wheel.</summary>
        private List<Part> wheelParts;

        /// <summary>For how many checks of the hold in a row every wheel of the held vessel has been built.</summary>
        private int builtChecks;

        /// <summary>The tyre force of each of <see cref="wheelParts"/> (same index) at the last check of the hold, or
        /// null when its wheel was not on the ground.</summary>
        private Vector2?[] lastTyreForces;

        private int heldSteps;

        /// <summary>The id of the control lock set while the vessel is held, or null when none is set.</summary>
        private string lockId;

        /// <summary>Whether the vessel is held still, until its wheels hold it.</summary>
        public bool IsHeld => held != null;

        protected override void OnStart()
        {
            GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
        }

        private void OnDestroy()
        {
            // The vessel goes away with its bodies held, merged into another by a docking for instance: its parts must
            // not stay held, nor the controls locked.
            Forget();
            GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
        }

        public override void OnGoOnRails()
        {
            // Packing the vessel takes its bodies over.
            bool wasHeld = IsHeld;
            Forget();
            if (wasHeld)
            {
                OnLetGo.Fire(vessel);
            }
        }

        private void OnVesselGoOffRails(Vessel v)
        {
            if (v != vessel || !CanHold || !vessel.LandedOrSplashed || !HasWheels())
            {
                return;
            }
            Hold();
        }

        /// <summary>Whether a part of the vessel has a wheel (landing leg, wheel, landing gear).</summary>
        private bool HasWheels()
        {
            foreach (Part p in vessel.parts)
            {
                if (p.FindModuleImplementing<ModuleWheelBase>() != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Called once the game has built the wheel of <paramref name="part"/>: keeps its part held if the vessel is.</summary>
        internal void OnWheelBuilt(Part part)
        {
            // Building the wheel makes its part's body dynamic again.
            if (held != null && part.rb != null && !part.rb.isKinematic)
            {
                part.rb.isKinematic = true;
                held.Add(part.rb);
            }
        }

        /// <summary>
        /// Holds every body of the vessel still (kinematic), until <see cref="CheckHold"/> lets it go.
        /// </summary>
        private void Hold()
        {
            held = new List<Rigidbody>();
            wheelParts = new List<Part>();
            builtChecks = 0;
            heldSteps = 0;
            foreach (Part p in vessel.parts)
            {
                if (p.rb != null && !p.rb.isKinematic)
                {
                    p.rb.isKinematic = true;
                    held.Add(p.rb);
                }
                if (p.servoRb != null && !p.servoRb.isKinematic)
                {
                    p.servoRb.isKinematic = true;
                    held.Add(p.servoRb);
                }
                if (p.FindModuleImplementing<ModuleWheelBase>() != null)
                {
                    wheelParts.Add(p);
                }
            }
            lastTyreForces = new Vector2?[wheelParts.Count];
            // The same controls as the game locks while it eases a vessel in (VesselPrecalculate.StartEasing), on the
            // active vessel only, under a lock of this vessel's own.
            if (vessel == FlightGlobals.ActiveVessel)
            {
                lockId = "KSPFixPhysicsEasingHold" + vessel.persistentId;
                InputLockManager.SetControlLock(ControlTypes.ALL_SHIP_CONTROLS, lockId);
            }
            Log.Debug($"{Name()}: held still at {Time.fixedTime:0.00}, {held.Count} body(ies), {wheelParts.Count} wheel(s)" +
                (lockId != null ? ", controls locked" : ""));
            OnHold.Fire(vessel);
            // Checked before the wheels' own physics step, so that the step that lets go is the first one their
            // suspensions and tyres push the vessel in: VPP (KSPWheelController) and the part modules run their
            // FixedUpdate after TimingStage.Early, and while the bodies are still held their forces are lost.
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Early, CheckHold);
        }

        /// <summary>
        /// At each physics step of the hold, before the wheels' own: lets the held vessel go, at rest, once the contacts
        /// its wheels found are known and either none touches the ground or every tyre on the ground has settled
        /// (<see cref="TyresSettled"/>); or after <see cref="MaxHoldSteps"/>.
        /// </summary>
        private void CheckHold()
        {
            if (held == null)
            {
                TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Early, CheckHold);
                return;
            }
            heldSteps++;
            bool built = true;
            foreach (Part p in wheelParts)
            {
                ModuleWheelBase wheelBase = p != null ? p.FindModuleImplementing<ModuleWheelBase>() : null;
                if (wheelBase != null && wheelBase.Wheel == null)
                {
                    built = false;
                }
            }
            // A wheel seen built at a check goes through its first physics step at that step; VPP reads the contact it
            // found at the next step, after the next check: the check after that is the first to see it.
            builtChecks = built ? builtChecks + 1 : 0;
            bool contactsKnown = builtChecks >= 3;
            int grounded;
            bool settled = TyresSettled(out grounded);
            // A vessel none of whose wheels touches the ground rests on its parts: holding it longer only keeps it from
            // making its contacts with the ground.
            bool ready = contactsKnown && (grounded == 0 || settled);
            if (!ready && heldSteps < MaxHoldSteps)
            {
                return;
            }
            Forget();
            Log.Info($"{Name()}: let go after {heldSteps} physics step(s) held, {grounded} of {wheelParts?.Count ?? 0} " +
                $"wheel(s) on the ground, wheel contacts {(contactsKnown ? "known" : "not known")}, tyres " +
                $"{(settled ? "settled" : "not settled")}");
            OnLetGo.Fire(vessel);
        }

        /// <summary>Lets the held bodies go, if any, unlocks the controls, and forgets the hold.</summary>
        private void Forget()
        {
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Early, CheckHold);
            if (lockId != null)
            {
                InputLockManager.RemoveControlLock(lockId);
                lockId = null;
            }
            if (held != null)
            {
                foreach (Rigidbody rb in held)
                {
                    if (rb != null)
                    {
                        rb.isKinematic = false;
                    }
                }
            }
            held = null;
            lastTyreForces = null;
        }

        /// <summary>
        /// Whether the force of every tyre of the held vessel on the ground is within <see cref="TyreTolerance"/> of the
        /// force it is heading for, judged from how much it changed since the last check; false for a tyre first seen on
        /// the ground. Counts the wheels on the ground into <paramref name="grounded"/>, and remembers the forces for the
        /// next check.
        /// </summary>
        private bool TyresSettled(out int grounded)
        {
            bool settled = true;
            grounded = 0;
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < wheelParts.Count; i++)
            {
                Part p = wheelParts[i];
                ModuleWheelBase wheelBase = p != null ? p.FindModuleImplementing<ModuleWheelBase>() : null;
                KSPWheelController wheel = wheelBase != null ? wheelBase.Wheel : null;
                if (wheel == null || wheel.currentState == null || !wheel.currentState.grounded)
                {
                    lastTyreForces[i] = null;
                    continue;
                }
                grounded++;
                Vector2 force = wheel.currentState.tireForce;
                Vector2? last = lastTyreForces[i];
                lastTyreForces[i] = force;
                if (last == null)
                {
                    settled = false;
                    continue;
                }
                // VehicleBase.ApplyTireForce moves the applied force towards the solver's by a share of the gap at each
                // step (sideways twice, with the friction sharpness first): the change over a step divided by that
                // share is how far the force still is from the one it is heading for.
                float forward = wheel.tireForceSmoothing ? Mathf.Clamp01(wheel.tireForceSharpness * dt) : 1f;
                float sideways = forward * (wheel.tireSideDeflection ? Mathf.Clamp01(wheel.tireSideDeflectionRate * dt) : 1f);
                Vector2 change = force - last.Value;
                float gapForward = forward > 0f ? Mathf.Abs(change.y) / forward : float.MaxValue;
                float gapSideways = sideways > 0f ? Mathf.Abs(change.x) / sideways : float.MaxValue;
                float allowed = Mathf.Max(TyreTolerance * force.magnitude, TyreForceFloor);
                if (gapForward > allowed || gapSideways > allowed)
                {
                    settled = false;
                }
            }
            return settled;
        }

        private string Name()
        {
            return vessel != null ? vessel.GetDisplayName() : "?";
        }
    }
}
