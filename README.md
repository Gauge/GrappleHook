# Grapple Hook

A Space Engineers mod that adds a grapple hook launcher. It fires a hook on a rope, the rope
pulls the two grids together under a damped spring, a winch reels it in or pays it out, and
players can clip onto the rope and ride it like a zipline.

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

While you control a turret directly (Control in its terminal), right click lets go, holding W
pays rope out at the full loosen speed and holding S reels it in at the full tighten speed;
letting go of the key stops the winch. These follow the game's key bindings (secondary action,
forward, backward). Taken over from a cockpit, the game also passes W and S to that ship's
thrusters, which a mod cannot stop.

The grapple's own terminal actions (Detach, Tighten, Loosen, Reset) have toolbar icons in the
style of the game's, drawn by [tools/model/action_icons.py](tools/model/action_icons.py).

## Repo layout

Laid out the way [Thermodynamics](../Thermodynamics) is: the repository holds the mod project,
and **only the mod project folder ships**.

| Path | What it is |
| --- | --- |
| [GrappleHook/](GrappleHook) | The mod project (MDK2). Everything here that [mdk.ini](GrappleHook/mdk.ini) does not ignore is packed into the game's `Mods/GrappleHook`. |
| [GrappleHook/*.cs](GrappleHook) | Session component, block logic, terminal controls, settings, networking. Packed to `Data/Scripts/GrappleHook/`. |
| [GrappleHook/Core/](GrappleHook/Core) | The pure part: rope shape, winch and tension math, zipline pulley math. No game API, no session, no entity. Ships with the mod and is compiled by the game like everything else. |
| [GrappleHook/SENetworkAPI/](GrappleHook/SENetworkAPI) | Vendored copy of SENetworkAPI, used for all client/server state sync. |
| [GrappleHook/Content/](GrappleHook/Content) | `Data/` (block, weapon, ammo and blueprint definitions), `Models/`, `Textures/`. Packed to the mod root. |
| [tests/](tests) | Projects that link the same `Core` sources so they can be built and tested outside the game, plus the definition checks. |
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
`Storage/3280424480.sbm_hook/` to `Storage/3280424480.sbm_GrappleHook/` with this update; a server
that customised it must copy the file across, or it starts from defaults.

## Why `Core` is separate

Everything the game supplies - entities, physics, players, the session - stays out of
`Core`. The only Space Engineers assembly it references is `VRage.Math.dll`, which is pure
managed math and loads fine on .NET on Linux, so the rope and zipline behaviour can be
tested in milliseconds instead of by loading a world. Core references that one assembly on purpose
rather than every game assembly: a game type used in Core fails to compile in the test build.

That is not the whole contract: the game also applies a type whitelist, which the mod build checks
(`MDK01`), and some behaviour only shows in a running world - see Running in game. `LangVersion` is
pinned to 6 everywhere the mod's sources compile, the version the in-game script compiler accepts.
