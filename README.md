# Grapple Hook

A Space Engineers mod that adds a grapple hook launcher. It fires a hook on a rope that holds
whatever it catches like a real rope, a winch reels it in or pays it out, and players can clip
onto the rope and ride it like a zipline.

* **Namespace:** `GrappleHook`
* **Network channel ID:** `32144`
* **Target:** Space Engineers 1 (`net48`, C# 6), built with MDK2

## Blocks

| Block | Subtype | Base type | Grid | Aiming |
| --- | --- | --- | --- | --- |
| Grapple Hook Turret | `GrappleHookTurretLarge` | `LargeMissileTurret` | Large | Traverses on its own two axes |
| Grapple Hook Turret | `GrappleHookTurretSmall` | `LargeMissileTurret` | Small | Traverses on its own two axes |
| Grapple Hook Launcher | `GrappleHookFixedLarge` | `SmallMissileLauncher` | Large | Fixed, fires along block forward |
| Grapple Hook Launcher | `GrappleHookFixedSmall` | `SmallMissileLauncher` | Small | Fixed, fires along block forward |

All four run the same game logic ([WeaponControlLayer.cs](GrappleHook/WeaponControlLayer.cs));
only the block specific setup and terminal registration differ.

The small turret is 5x5x5 small blocks, the vanilla small turret footprint, with its own models
built by [tools/model](tools/model/README.md). Its launcher is the large turret's size, because it
fires the same hook. It pairs with the large turret (`BlockPairName` `GrappleHook`), so the size
switch swaps between them.

The fixed variants are launch tubes sized like the vanilla fixed rocket launchers: 1x1x2 on a
large grid, 1x1x4 on a small one, with their own models built by
[tools/model](tools/model/README.md). Each model's `muzzle_projectile_001` dummy sets where the
hook rests in the tube: the game takes a gun's muzzle from the dummy matching its ammo type, and
the grapple ammo is a projectile type. On the large launcher the hook sits inside the tube; on
the small one its barbed head stands out of the front, as on the turrets.
`SmallMissileLauncher` hardcodes conveyor feeding to large grid only, so the small grid launcher
has to be loaded by hand.

Like the vanilla fixed guns, the fixed launchers can be selected from a cockpit's toolbar as a
weapon group ([BlockCategories.sbc](GrappleHook/Content/Data/BlockCategories.sbc) adds them to the
ship weapons). Left click then hooks with every launcher of that type on the cockpit's grid, and
right click lets go. Left click fires the vanilla gun, which spends a hook magazine; the mod
launches the hook when the gun starts shooting, so setting `Shoot` from a script launches it too.
The game never hands the right click to a missile launcher, so the mod reads that input itself,
as it does for a turret under direct control.

While you control a turret directly (Control in its terminal), right click lets go, holding
Page Up reels the rope in at the full tighten speed and holding Page Down pays it out at the full
loosen speed; letting go of the key stops the winch. Right click follows the game's binding for
the secondary action. The winch keys are Page Up and Page Down rather than W and S because a turret
taken over from a cockpit still passes the movement keys to that ship's thrusters, which a mod
cannot stop; the game binds nothing to Page Up and Page Down.

The grapple's own terminal actions (Detach, Tighten, Loosen, Reset) have toolbar icons in the
style of the game's, drawn by [tools/model/action_icons.py](tools/model/action_icons.py).

## The rope

[WeaponControlLayer.Rope.cs](GrappleHook/WeaponControlLayer.Rope.cs) is the rope; the math under
it is in [Core](GrappleHook/Core) and unit tested.

**Holding.** The rope is a length paid out between the muzzle and the hook. It pulls only when the
two are further apart than that, like a real rope, and never pushes. The pull is a stiff, damped
constraint scaled to the mass on its ends (`Rope.SpringFrequency`, `Rope.DampingRatio`), so it
behaves the same on a 20 kg block as on a capital ship: no shaking on light grids, no bungee
bounce on heavy ones. The mass on each end counts everything joined to it by rotors, pistons,
hinges and connectors, and anything static, or joined to something static, is an anchor. The
damping works on how fast the rope itself is stretching, from each end's velocity at the point
the rope pulls on, so two ships towing side by side are not slowed down. Measured in game: a
26 t payload on a 13 m rope swings as a pendulum and stretches it 0.3 m at the bottom of the
swing; a 20 kg block stretches its rope 0.25 m.

**The winch** reels in at up to `Winch.TightenSpeed` and pays out at up to `Winch.LoosenSpeed`.
It can only pull so hard: `Winch.ForceLargeGrid` for a launcher on a large grid,
`Winch.ForceSmallGrid` on a small one. A load heavier than that stalls the winch, and its brake
still holds it. Tightening a rope already shorter than `Rope.MinLength` leaves it alone rather
than paying it out to the minimum.

**Breaking** (`Rope.Breaking`, off by default) snaps the rope when its tension passes the
strength for its grid size, and the hook is lost. The hardest pull a rope sees is catching a load
that has fallen into its slack: in the test world a 26 t block falling 6.5 m/s pulled 2.4 MN for
an instant. Set the strengths with that in mind.

**Wrapping** (`Rope.Wrapping`, off by default) lets the rope catch on grids and voxels between
the launcher and the hook. The server tests each span of the rope against the world every tick.
When one swings into something, it finds the edge the rope first touched and bends the rope
there. The rope lets go of the edge once it swings back past straight. A taut rope presses into
each edge it bends around, so a free grid it is wrapped over is pushed. Each wrap point moves
with what it is on, and the server sends them to clients.

**The hook** leaves with the launcher's own velocity plus `Hook.Speed`, and with `Hook.Gravity`
(off by default) it falls in flight, so far targets need aiming above. One that catches nothing
by `Hook.Range` gives up. After a detach, a miss, or its target being destroyed, the hook is
reeled back to the launcher at `Hook.RetractSpeed`, keeping up with the launcher if it moves.
While caught, the hook model moves and turns with what it is caught in.

**What you see** is drawn after the physics step, so the rope stays on its ends on a fast ship.
On a client it is a chain of points that swings under gravity and its own momentum: it settles
into the hanging curve in gravity, and in space a slack rope floats instead of sagging. A
dedicated server draws nothing and only works out the exact hanging curve (a catenary) when
someone is riding the rope.

**Riding** (F while looking at the rope) clips the player on at a distance along the rope, so
they travel with the rope when it moves. Gravity along the rope speeds them up downhill, and
`Zipline.MinSpeed` keeps them moving.

**Cost.** A rope off screen or more than 2.5 km from the camera is not built or drawn. A dedicated
server builds no rope shape at all unless someone is riding it. A slack rope skips the force
work entirely, mass lookups happen once a second, and nothing allocates per frame. Wrapping, when
it is on, costs one ray per span of rope per tick on the server.

**What it is sized for.** The defaults come from 8,122 workshop ships, the blueprint corpus the
Thermodynamics mod surveys, weighed by their build components (empty inventories, so real ships
are heavier):

| | Small grid ships | Large grid ships |
| --- | --- | --- |
| Mass, middle half | 25 t to 103 t (median 49 t) | 570 t to 5.4 kt (median 1.8 kt) |
| Length, median / 95th percentile | 12.5 m / 37 m | 105 m / 365 m |
| Full thrust, median | 2.8 MN | 74 MN |
| Have rotor, piston, hinge or connector parts | 57% | 50% |

The winch on each grid size lifts 40% of the ships of that size straight up against 1 g
(11.5 MN large, 0.37 MN small); in space it moves anything, heavier ships slower. With breaking on,
a rope holds against the full thrust of 65% of the ships of its size, so only the most powerful
35% can break free (113 MN large, 4.3 MN small). `Rope.MinLength` is 3 m, as a third of small ships
are shorter than 10 m. Pulling through a rotor holds: in the test world a 412 t block hung from an
850 kg rotor head, swinging at 19 m/s with about 11 MN through the joint, and the head did not
move on its stator.

## Settings

The server keeps them in its world, as `Storage/<mod storage folder>/GrappleHook.cfg`, and syncs
them to every client. The file is grouped into `Rope`, `Winch`, `Hook` and `Zipline`, with a
comment above each value giving its meaning and units. Edit it with the world closed. Deleting a
value brings back its default, a value out of range is clamped (the game log says which), and the
file is rewritten on load, so values added by a newer version appear with their comments. A file
the mod cannot read is kept as `GrappleHook.unreadable.cfg` and the defaults are used.

The flat file of version 11 and earlier is one of those: its values do not carry over, because
most of them changed meaning (the rope's fixed spring rate became a winch strength, and the hook
speed is in m/s instead of metres per tick).

## Repo layout

Laid out the way [Thermodynamics](../Thermodynamics) is: the repository holds the mod project,
and **only the mod project folder ships**.

| Path | What it is |
| --- | --- |
| [GrappleHook/](GrappleHook) | The mod project (MDK2). Everything here that [mdk.ini](GrappleHook/mdk.ini) does not ignore is packed into the game's `Mods/GrappleHook`. |
| [GrappleHook/*.cs](GrappleHook) | Session component, block logic, terminal controls, settings, networking. Packed to `Data/Scripts/GrappleHook/`. |
| [GrappleHook/Core/](GrappleHook/Core) | The pure part: the rope's hanging shape and its swinging chain, tension and winch math, wrap geometry, zipline math. No game API, no session, no entity. Ships with the mod and is compiled by the game like everything else. |
| [GrappleHook/SENetworkAPI/](GrappleHook/SENetworkAPI) | Vendored copy of SENetworkAPI, used for all client/server state sync. |
| [GrappleHook/Content/](GrappleHook/Content) | `Data/` (block, weapon, ammo and blueprint definitions), `Models/`, `Textures/`. Packed to the mod root. |
| [tests/](tests) | Projects that link the same `Core` sources so they can be built and tested outside the game, plus the definition checks. |
| [tests/GrappleHook.Benchmarks/](tests/GrappleHook.Benchmarks/README.md) | Offline timing of the per-frame rope math, against a frozen baseline copy of `Core`. |
| [tools/validate.sh](tools/validate.sh) | Compile everything and run the tests, without deploying. |
| [tools/live-game/](tools/live-game) | Boot the game headless into a test world and report what it made of the mod. |
| [tools/model/](tools/model/README.md) | Builds the small grid turret's models, icon and build stages from Blender scripts, through the game's own model exporter. |
| [Workshop/](Workshop) | `metadata.mod` and `modinfo.sbmi` (Workshop id `3280424480`). Kept outside the packed folder; see [Publishing](#publishing). |

## Building

**`GrappleHook/GrappleHook.csproj` is the mod project, and it is an MDK2 project.** Space Engineers
compiles the mod's sources itself at world load; the assembly the project produces is never shipped
or loaded. What the project gives is three MDK2 packages:

| Package | What it does here |
| --- | --- |
| `Mal.Mdk2.References` | Finds the installed game and references its assemblies from `Bin64/`. |
| `Mal.Mdk2.ModAnalyzers` | Checks every source against the game's script whitelist and reports a prohibited type or member as `MDK01`, an error. It skips `*.debug.cs`. |
| `Mal.Mdk2.ModPackager` | Packs the mod into the game's local `Mods/GrappleHook` unless `MdkBuildConfiguration=NoPackage` is set. |

```bash
bash tools/validate.sh              # compile the mod and tests (no deploy), then run the tests
bash tools/validate.sh --build-only # compile only
dotnet build GrappleHook.sln        # compile everything AND deploy the mod to the game
dotnet build GrappleHook/GrappleHook.csproj   # deploy the mod alone
dotnet run -c Release --project tests/GrappleHook.Benchmarks   # time the rope math; see its README
```

`tests/GrappleHook.slnx` includes the mod project, so the validation build compiles every file the
game will compile and runs the whitelist check over it. Run `dotnet restore tests/GrappleHook.slnx`
once, and again whenever a package reference changes.

### Machine-specific paths

[`GrappleHook/mdk.ini`](GrappleHook/mdk.ini) is shared. Game and output paths go in
`GrappleHook/mdk.local.ini`, which `.gitignore` keeps out of the repository:

```ini
[mdk]
binarypath=/path/to/SpaceEngineers/Bin64
output=/path/to/SpaceEngineers/Mods
```

`output` names the parent `Mods` folder; MDK2 appends `GrappleHook`. On this machine that is the
Proton prefix's `drive_c/users/steamuser/AppData/Roaming/SpaceEngineers/Mods`. The test projects
read `binarypath` from the same file to find `VRage.Math.dll`; `SE_BIN=/path/to/Bin64` overrides it.

To open the project in MDK Hub, choose **Add Existing Project** and select
`GrappleHook/GrappleHook.csproj`.

## Running in game

**Building the mod project deploys it.** Then enable the local mod "GrappleHook" in a world's mod
list. Script and definition changes take effect on world reload.

To test without touching the desktop, [tools/live-game](tools/live-game/README.md) deploys, boots
the game in a headless gamescope into a disposable `GrappleLive` world with a rig of grapple blocks,
and prints the mod's compile errors, exceptions and log lines:

```bash
tools/live-game/lt up       # deploy, boot, load GrappleLive; prints errors if any
tools/live-game/lt log      # every GrappleHook / NetworkAPI line this session
tools/live-game/lt reload   # redeploy and restart
tools/live-game/lt down
```

## Publishing

Publish from the game's mod list as usual. The game needs `modinfo.sbmi` in the local mod folder to
update Workshop item `3280424480` rather than create a new one: copy
[`Workshop/metadata.mod`](Workshop/metadata.mod) and [`Workshop/modinfo.sbmi`](Workshop/modinfo.sbmi)
into `Mods/GrappleHook/` once. `mdk.ini`'s `donotclean` keeps them there across later deploys.

**The scripts folder changed from `Data/Scripts/hook` to `Data/Scripts/GrappleHook`.** The game
names a mod's world storage after that folder, so a server's `GrappleHook.cfg` moves from
`Storage/3280424480.sbm_hook/` to `Storage/3280424480.sbm_GrappleHook/` with this update. Its
values would not carry over anyway (see [Settings](#settings)), so a server that customised it
starts from the new defaults and sets them again in the new layout.

## Why `Core` is separate

Everything the game supplies - entities, physics, players, the session - stays out of
`Core`. The only Space Engineers assembly it references is `VRage.Math.dll`, which is pure
managed math and loads fine on .NET on Linux, so the rope and zipline behaviour can be
tested in milliseconds instead of by loading a world. Core references that one assembly on purpose
rather than every game assembly: a game type used in Core fails to compile in the test build.

That is not the whole contract: the game also applies a type whitelist, which the mod build checks
(`MDK01`), and some behaviour only shows in a running world - see Running in game. `LangVersion` is
pinned to 6 everywhere the mod's sources compile, the version the in-game script compiler accepts.
