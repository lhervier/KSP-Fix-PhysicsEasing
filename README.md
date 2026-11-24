# Physics Easing Fix

**⚠️ Not finished yet.** This is a new fix, not a finished mod. The code and the conclusions on this page can still
change.

A fix for stock KSP 1.12: a few Harmony patches and a vessel module. It is a mod of its own, built on top of
[KSP Community Fixes](https://github.com/KSPModdingLibs/KSPCommunityFixes), the base most players run. It is meant as
a base under other fixes rather than for itself: [Suspension Bounce Fix](https://github.com/lhervier/KSP-Fix-SuspensionBounce),
[Landed Shape Fix](https://github.com/lhervier/KSP-Fix-LandedShape) and
[Docking Snap Fix](https://github.com/lhervier/KSP-Fix-DockingSnap) need it.

> **When physics starts on a vessel standing on the ground or floating, and when two vessels dock, KSP takes the
> vessel's weight away and brings it back over a few physics steps: the *physics easing*.** This mod keeps the
> vessel's full weight instead, and holds a vessel on wheels or landing legs still until they hold it as physics
> starts on it.

**Why the easing was there.** It fixes nothing: it softens whatever goes wrong as physics starts on a vessel, and
keeps the game from reacting to it. Each of those causes now has a fix of its own, in
[KSP Community Fixes](https://github.com/KSPModdingLibs/KSPCommunityFixes), in this mod or in the fixes next to it:
see [What the easing stood in for](#what-the-easing-stood-in-for).

**How this was made.** The investigation and the code were written with Claude, Anthropic's AI assistant. Everything
here was reviewed and validated by a human — me. I am saying so up front, because contributions made with an AI
deserve a closer look than others, and because some people would rather stop reading here. The stock code involved
is named and quoted in these pages; the vessel module is the part that deserves the closest look.

## Why it matters

The easing hides how a vessel on wheels starts, and it gets in the way of the fixes that bring a vessel back as it
was saved or dock two vessels where they stand:

- **It hides a vessel left without wheels for a physics step.** The game builds the wheels a physics step late, and
  their tyres bring their force up from nothing: a vessel given its weight at once would fall for a step, and slide
  on a slope while its tyres catch up. The easing makes that step nearly weightless instead of fixing it.
- **It takes the weight off a vessel put back at rest.** A vessel brought back in the shape its weight bent it to,
  on suspensions set for that weight, is at rest with its full weight, not without it: weightless, its suspensions
  push it up and its joints relax, and it bounces as its weight comes back.
- **It takes the weight off a vessel that has just docked.** When the two vessels dock where they stand, nothing has
  moved and there is no jolt to soften: weightless, a vessel standing on its wheels or legs is pushed up by them, and
  bounces. Where the game snaps one vessel into line with the other, the easing is too short to soften the snap.

## What the easing stood in for

The game's own physics settings give its purpose, in the line that makes the buildings of the space centre
invulnerable while it runs:

```
// Physics.cfg (KSP 1.12)
buildingEasingInvulnerableTime = 2 // Seconds buildings stay invulnerable for when the active vessel goes off rails, to protect against physics jerks
```

While it eases a vessel in, the game also holds back what a jerk would set off:

- `Vessel.checkLanded` leaves the vessel landed, even if it lifts off the ground;
- `Vessel.AddRBAnchor` does not anchor a slow landed vessel to the ground;
- `Vessel.updateSituation` keeps a vessel waiting on the launch pad or the runway waiting, whatever its speed;
- the controls of the active vessel are locked;
- the buildings of the space centre are invulnerable for 2 s (`DestructibleBuilding.OnPhysicsEase`).

**One of the jerks came from how parts are created.** In
[KSP Community Fixes issue #9](https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/9), its maintainer puts
the vessels torn apart at flight scene loads down, most likely, to `Part.Start`: a coroutine that sets up a part's
rigidbodies, then its joints, a visual frame apart, while under load several physics steps can run in a single
frame. He counts the easing among the checks that keep physics from kicking in before everything is set up, and could
reproduce the tear-apart only on loads that start no easing. KSP Community Fixes fixes it at the root: its
`PartStartStability` patch makes `Part.Start` wait for the physics steps instead of the visual frames. It leaves the
easing itself as it is.

**What fixes each of them now:**

| What the easing softened or held back | What fixes it |
|---|---|
| Joints set up after the rigidbodies at a part's creation | KSP Community Fixes (`PartStartStability`) |
| The ground rebuilt at another height at each load | [Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision) |
| Wheels built a physics step late, tyres starting from nothing | This mod: the vessel is held until its wheels hold it |
| A landed vessel loaded in its built shape, its suspensions set again | [Landed Shape Fix](https://github.com/lhervier/KSP-Fix-LandedShape) |
| A vessel on the launch pad taken as launched | This mod: the held vessel's orbit is given the velocity of a vessel at rest |
| The snap of one vessel into line with the other at a docking | [Docking Snap Fix](https://github.com/lhervier/KSP-Fix-DockingSnap) |

With those, the easing only gets in the way, as [Why it matters](#why-it-matters) shows: this mod removes it. It keeps
one thing the easing did: the controls of the active vessel stay locked while the vessel is held, as they were while
it was eased in.

## The culprits

### The weight is eased in

`VesselPrecalculate.CalculateGravity` starts the easing in two cases: when a landed or splashed vessel leaves the
rails, at a load, at the end of a time warp, or when you come back within 200 m of it; and when two vessels have just
docked (`VesselPrecalculate.onDockingComplete`), whether on the ground or in flight. The vessel's gravity then starts
at nothing and grows at every physics step until it is whole.

```csharp
// VesselPrecalculate.StartEasing (KSP 1.12)
vessel.gravityMultiplier = 0.0;

// VesselPrecalculate.CalculateGravity (KSP 1.12), at each physics step until the multiplier reaches 1
vessel.gravityMultiplier += easingFrameIncrease;
```

### The wheels are built a physics step late

`ModuleWheelBase` builds a wheel by a coroutine that waits for its part to be unpacked: the frame after the unpack,
once a physics step has gone by with no wheel at all. The wheel then finds the ground only after a physics step of its
own.

### The tyres start from nothing

A tyre does not push with the force the wheel's solver finds, but moves towards it by a share of the gap at each
physics step. Once its wheel touches the ground, a tyre needs several steps to hold the vessel: on a slope, a vessel
free to move slides meanwhile, and a sliding tyre grips less than a still one.

```csharp
// VehicleBase.ApplyTireForce (KSP 1.12): the force a tyre applies moves towards the one it should push by a share of
// the gap at each physics step
wheel.tireForce.x = Mathf.Lerp(wheel.lastTireForce.x, wheel.tireForce.x, tireForceSharpness * Time.deltaTime);
wheel.tireForce.y = Mathf.Lerp(wheel.lastTireForce.y, wheel.tireForce.y, tireForceSharpness * Time.deltaTime);
```

## The fix

- A prefix of `VesselPrecalculate.StartEasing` skips every easing of the game: a vessel leaving the rails landed or
  splashed starts with its full weight, and a vessel just docked keeps it.
- A vessel module holds a landed or splashed vessel with wheels still (its rigidbodies kinematic) as it leaves the
  rails, until the game has built its wheels and they have found the ground, and the force of each tyre on the ground
  has reached the one that holds the vessel (a few seconds at most). A vessel none of whose wheels touches the ground
  rests on its parts: it is let go as soon as that is known.
- It lets the vessel go at the start of a physics step, before the wheels' own, so that their suspensions and tyres
  push it from its first free step.
- While the active vessel is held, its controls are locked, as the game locks them during the easing: an engine
  fired or a decoupler fired then would push nothing.
- A postfix of `ModuleWheelBase.wheelSetup` keeps a wheel's part held as the game builds the wheel, which makes its
  body dynamic again.
- While a vessel is held, a prefix of `OrbitDriver.TrackRigidbody` gives its orbit the velocity of a vessel at rest on
  the surface. The game takes a vessel's velocity from its rigidbodies only when its root part is not kinematic, and
  otherwise keeps a stale one: the vessel would read hundreds of metres per second at one step out of two, and a
  vessel waiting on the launch pad or the runway would be taken as launched. The easing hid it, the game not testing
  that speed while easing.

### Two events for other mods

While a vessel is held, its rigidbodies are kinematic: a velocity or a force another mod gives them is lost. So the
mod fires two `GameEvents`, with the vessel:

- **`onPhysicsEasingHold`** as it holds the vessel, once its rigidbodies are kinematic, from the game's
  `onVesselGoOffRails`.
- **`onPhysicsEasingLetGo`** as it lets the vessel go, at the start of a physics step, before the wheels' own, once its
  rigidbodies are free again; and if the vessel goes back on the rails while held. It is not fired for a vessel
  destroyed while held: its own vessel modules go with it.

Both are registered when KSP starts, and are found by name: a mod needs neither a reference to this one nor this one
installed. [Landed Shape Fix](https://github.com/lhervier/KSP-Fix-LandedShape) gives the parts their saved velocities
as the vessel is let go.

The game does not say in which order it calls the handlers of `onVesselGoOffRails`: in its own handler, a mod cannot
tell yet whether the vessel will be held. It can from the next physics step on, as Landed Shape Fix does.

```csharp
public class MyVesselModule : VesselModule
{
    // Null when Physics Easing Fix is not installed.
    private EventData<Vessel> onHold;
    private EventData<Vessel> onLetGo;

    private bool held;

    protected override void OnStart()
    {
        onHold = GameEvents.FindEvent<EventData<Vessel>>("onPhysicsEasingHold");
        onLetGo = GameEvents.FindEvent<EventData<Vessel>>("onPhysicsEasingLetGo");
        // GameEvents take instance methods only.
        onHold?.Add(OnHold);
        onLetGo?.Add(OnLetGo);
    }

    private void OnDestroy()
    {
        onHold?.Remove(OnHold);
        onLetGo?.Remove(OnLetGo);
    }

    private void OnHold(Vessel v)
    {
        if (v == vessel)
        {
            held = true;
        }
    }

    private void OnLetGo(Vessel v)
    {
        if (v == vessel)
        {
            held = false;
            // The rigidbodies are free again: a velocity given here is the one the vessel starts with.
        }
    }
}
```

## Limits and solutions

- **A vessel just docked is not held.** The game's snap of one vessel into line with the other as they dock is left
  as it is: [Docking Snap Fix](https://github.com/lhervier/KSP-Fix-DockingSnap) docks the vessels where they stand
  instead.
- **The buildings of the space centre are no longer invulnerable for 2 s** as the active vessel starts, as they are
  during the easing.

## Work in progress

What a player would see if this mod broke something, or what it was not checked with yet, and how to try it by hand,
with the mod and without:

- **A splashed vessel**, a boat or a seaplane on its floats: it should load and float as without the mod, without a
  jump.
- **A vessel waiting on the launch pad or the runway**, saved and loaded: it should still be waiting there, not
  counted as launched.
- **A robotic part moving when the game is saved**, on a vessel with wheels: it should go on moving to its target.
- **A vessel on stock LT-05 landing legs**: their tyres catch up slowly, and the vessel should be held a little longer,
  then stand still.
- **A docking**, between two vessels on the ground or in orbit, with Docking Snap Fix and without: the vessels should
  dock as without the mod, a vessel on wheels or legs without a bounce.
- **A large vessel held together by autostruts**, saved on the ground and loaded: the game builds its autostruts again
  a little after physics starts, now under the vessel's full weight; it should stand as without the mod, not bent.
- **A vessel loaded against a building of the space centre**, or a rover parked on the launch pad: the building should
  take no damage.

## Install

Copy the `GameData\KSPFixPhysicsEasing` folder into the `GameData` folder of KSP. It needs
[Harmony](https://github.com/KSPModdingLibs/HarmonyKSP), as KSP Community Fixes does. It was made and tested with
KSP Community Fixes, [Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision) and
[Auto Strut Fix](https://github.com/lhervier/KSP-Fix-AutoStrut). Without KSP Community Fixes and its
`PartStartStability` patch, nothing replaces what the easing hid at a flight scene load
([What the easing stood in for](#what-the-easing-stood-in-for)).

## Settings

`GameData\KSPFixPhysicsEasing\PluginData\settings.cfg` is read once, when KSP starts:

```
// How much the mod writes to KSP.log: Error, Warning, Info or Debug.
logLevel = Info
```

At `Info`, the mod writes a line each time it lets a vessel it held go (`let go after N physics step(s) held`). At
`Debug`, it also writes one line each time it holds a vessel, and each time it skips the easing.

## Build

Set `KSPDIR` to the folder of a KSP install with Harmony in its `GameData`, then run `build.bat`: it compiles
`GameData\KSPFixPhysicsEasing\KSPFixPhysicsEasing.dll`. Installing is up to you.

## License

[MIT](LICENSE).
