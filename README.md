# Physics Easing Fix

**⚠️ Not finished yet.** This is a new fix, not a finished mod. The code and the conclusions on this page can still
change.

A fix for stock KSP 1.12: a few Harmony patches and a vessel module. It is a mod of its own, built on top of
[KSP Community Fixes](https://github.com/KSPModdingLibs/KSPCommunityFixes), the base most players run. It is meant as
a base under other fixes rather than for itself: [Suspension Bounce Fix](https://github.com/lhervier/KSP-Fix-SuspensionBounce)
and [Landed Shape Fix](https://github.com/lhervier/KSP-Fix-LandedShape) need it, and so will a docking fix in
preparation.

> **When physics starts on a vessel standing on the ground or floating, KSP takes its weight away and brings it back
> over a few physics steps: the *physics easing*.** This mod gives the vessel its full weight at once instead, and
> holds a vessel on wheels or landing legs still until they hold it.

**How this was made.** The investigation and the code were written with Claude, Anthropic's AI assistant. Everything
here was reviewed and validated by a human — me. I am saying so up front, because contributions made with an AI
deserve a closer look than others, and because some people would rather stop reading here. The stock code involved
is named and quoted in these pages; the vessel module is the part that deserves the closest look.

## Why it matters

The easing hides how a vessel on wheels starts, and it gets in the way of the fixes that bring a vessel back as it
was saved:

- **It hides a vessel left without wheels for a physics step.** The game builds the wheels a physics step late, and
  their tyres bring their force up from nothing: a vessel given its weight at once would fall for a step, and slide
  on a slope while its tyres catch up. The easing makes that step nearly weightless instead of fixing it.
- **It takes the weight off a vessel put back at rest.** A vessel brought back in the shape its weight bent it to,
  on suspensions set for that weight, is at rest with its full weight, not without it: weightless, its suspensions
  push it up and its joints relax, and it bounces as its weight comes back.

## The culprits

### The weight is eased in

When a landed or splashed vessel leaves the rails, at a load, at the end of a time warp, or when you come back within
200 m of it, `VesselPrecalculate.CalculateGravity` starts the easing: the vessel's gravity starts at nothing and grows
at every physics step until it is whole. The game does the same after a docking.

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

- A prefix of `VesselPrecalculate.StartEasing` skips the easing of every vessel leaving the rails landed or splashed:
  it starts with its full weight. The easing that follows a docking is left to the game.
- A vessel module holds a landed or splashed vessel with wheels still (its rigidbodies kinematic) as it leaves the
  rails, until the game has built its wheels and they have found the ground, and the force of each tyre on the ground
  has reached the one that holds the vessel (a few seconds at most). A vessel none of whose wheels touches the ground
  rests on its parts: it is let go as soon as that is known.
- It lets the vessel go at the start of a physics step, before the wheels' own, so that their suspensions and tyres
  push it from its first free step.
- A postfix of `ModuleWheelBase.wheelSetup` keeps a wheel's part held as the game builds the wheel, which makes its
  body dynamic again.
- It fires two `GameEvents` with the vessel, `onPhysicsEasingHold` as it holds it and `onPhysicsEasingLetGo` as it
  lets it go, which another mod can find by name (`GameEvents.FindEvent<EventData<Vessel>>`) without depending on
  this one: [Landed Shape Fix](https://github.com/lhervier/KSP-Fix-LandedShape) gives the parts their saved
  velocities as the vessel is let go.
- While a vessel is held, a prefix of `OrbitDriver.TrackRigidbody` gives its orbit the velocity of a vessel at rest on
  the surface. The game takes a vessel's velocity from its rigidbodies only when its root part is not kinematic, and
  otherwise keeps a stale one: the vessel would read hundreds of metres per second at one step out of two, and a
  vessel waiting on the launch pad or the runway would be taken as launched. The easing hid it, the game not testing
  that speed while easing.

## Limits

- **Only landed and splashed vessels.** A vessel in flight or in orbit is eased as without the mod.
- **The easing after a docking is left to the game.**
- **A vessel whose wheels do not touch the ground**, a base resting on its hull for instance, is held for the few
  steps it takes to know it.
- **The controls are not locked while a vessel is held**, as they are during the easing.

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

## Install

Copy the `GameData\KSPFixPhysicsEasing` folder into the `GameData` folder of KSP. It needs
[Harmony](https://github.com/KSPModdingLibs/HarmonyKSP), as KSP Community Fixes does. It was made and tested with
KSP Community Fixes, [Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision) and
[Auto Strut Fix](https://github.com/lhervier/KSP-Fix-AutoStrut).

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
