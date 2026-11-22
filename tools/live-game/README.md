# Live game harness

Runs Space Engineers for a live check of GrappleHook without touching the desktop: the game runs in
a headless gamescope, input is XTEST on gamescope's own XWayland display, and frames are read back
with XGetImage. Nothing appears on screen, the mouse and keyboard stay the user's, and audio goes to
a null sink. Adapted from Thermodynamics' `tools/live-game`: `live.sh` and `xse.c` are its files with
this repository's paths; `lt` and `world.py` are reduced to what this mod needs, without the command
bridge.

A session is for what only the game can show: whether the scripts compile under the game's own
compiler, whether the definitions load, and what the blocks' logic does once a world is running.

## Commands

```bash
tools/live-game/lt up                 # deploy, boot headless, load GrappleLive; ~60-110 s
tools/live-game/lt errors [n]         # compile errors, exceptions and crashes in the current log
tools/live-game/lt log [n]            # every GrappleHook / NetworkAPI line in the current log
tools/live-game/lt reload             # redeploy and restart; scripts compile at load
tools/live-game/lt shot [name] [scale]  # out/live-game/<name>.png
tools/live-game/lt say "/some chat line"
tools/live-game/lt world              # rebuild GrappleLive from the source world
tools/live-game/lt down
```

Environment switches for `lt world` / `lt up` (rebuild the world with `lt world` after changing them):

| Variable | Effect |
| --- | --- |
| `LT_MOD=local` (default) | This repository's build, deployed to `Mods/GrappleHook`. |
| `LT_MOD=workshop` | The published Workshop copy, through a `Mods/LT.3280424480` link into the workshop cache. Nothing is deployed. |
| `LT_MOD=workshop-debug` | A copy of the Workshop build with only `Tools.DebugMode` switched on, so the mod logs its state changes. |
| `LT_MOD=none` | The same world with no GrappleHook: the control run when deciding whether the mod is to blame. |
| `LT_PROBE=1` | Also loads `GrappleProbe` ([probe/](probe)): it loads magazines into every grapple block, fires them through the toolbar and the manual-trigger paths, detaches at 30 s, and logs each hook entity, `IsShooting` and ammo once a second (`[GrappleProbe]` lines in `lt log`). It also logs where each idle hook rests in its barrel's frame, paints the small turret red at 25 s, and parks the spectator camera for `lt shot`: on the small turret, then side-on at 10 s, close again at 25 s, then on the large and the small fixed launcher at 52 s and 60 s, their hooks back in the tubes, then back to the seat at 65 s. At 11.7 s it takes over the large turret (its terminal Control button) and logs its winch speed in each sample, for testing W and S (`live.sh hold w 3`). The manual-trigger step runs at 110 s. |
| `LT_DEBUG=1` | Switches on `Tools.DebugMode` in the deployed copy, so the mod logs its state changes, wraps (where, and how much rope) and snaps (tension, stretch, speed). |
| `LT_CONFIG=<file>` | Starts the world with that `GrappleHook.cfg`. It needs one `lt up` first, so the world's storage folder exists. |
| `LT_SUN_SHIFT=<minutes>` | Moves the world's clock on, which turns the sun: half of `SunRotationIntervalMinutes` (60) swaps night for day. 75 lights the rig from ToastyBugs' start. |
| `LT_PULSAR=1` | Launch through Pulsar with the player's plugin profile, as a normal Steam launch does. |
| `LIVE_CONTINUE_Y` | Height of Continue Game in the main menu, in 1/10000 of the window (default 5139 since update 1.211 moved it). |

**GrappleLive is a test world with a probe in it.** Loading it by hand (it shows in the Load Game list)
runs whatever mods the last `lt world` gave it, including the probe, which fires the turret on its own.

`live.sh` holds the lower-level commands (`start`, `stop`, `key`, `look`, `hold`, `click`, `snap`,
`log`); see the Thermodynamics harness README for them.

## The test world

`world.py build` copies the planet and player from `ToastyBugs` (only read; `LT_SOURCE` selects
another), drops every grid, loads the local `GrappleHook` mod alone, turns autosave off, and adds
`GrappleRig`: a static grid 15 m ahead of the player carrying a Grapple Hook Turret, a large Grapple
Hook Launcher, a battery and a vanilla gatling turret on an armour floor, facing a wall. On that
floor sits `GrappleRigSmall`, a static small grid with the small Grapple Hook Turret, a small
battery and the small Grapple Hook Launcher lying on it, both aimed at the same wall. Loading it runs every grapple block's `Init`, its NetSync setup
and the terminal-control registration.

The rig also has an open cockpit seat. Its toolbar holds the grapple actions: slots 1-4 for the
large turret, 5-8 for the large fixed launcher (Detach, Tighten, Loosen, Reset), and 9 selects
the large fixed launcher as a weapon group. A character's own toolbar cannot hold block actions,
so sit in the seat to use them. With `LT_PROBE=1` the probe seats the player at 3 s and hands
the camera back to the seat at 65 s. `live.sh key 9`, then `live.sh click <x> <y>` (left) and
`live.sh click <x> <y> 3` (right), drive the weapon group from there.

The test world also has `GrappleRigAir`, a static platform 150 m above the rig with two large
fixed launchers, and `GrappleRigAirSmall` beside it with a small one. With `LT_PROBE=1` the probe
hangs a free 26 t block (2x2x2 heavy armour) on `AIRLARGE`'s rope and a 20 kg block on
`AIRSMALL`'s, just before everything fires at 5 s. It winches both in from 15 s to 22 s, and the
30 s Detach drops them. `AIRLARGE` sits three cells back from the platform's front edge, so with
`Rope.Wrapping` on, its payload pulls the rope over that edge. `AIRMISS` fires at the open sky,
for the hook's range, its drop with `Hook.Gravity`, and its return. Ten times a second the probe
logs `PHYS` lines: each payload's distance from its muzzle, how far below it is, its speed, and
where its hook is. Example opt-in config for a run: copy the world's `GrappleHook.cfg`, set
`Wrapping/Enabled`, `Breaking/Enabled` and `Hook/Gravity` to `true`, and pass it as `LT_CONFIG`.

Ropes do not show in `lt shot` captures, for the published build as much as this one; the hook
entities do. Check ropes on a real screen.

The game keeps a binary copy of the world (`SANDBOX_0_0_0_.sbsB5`) after the first load and prefers
it, so edit the world with `lt world`, or delete that file after editing the `.sbs` by hand.

## Which profile it plays in

With Steam closed, a bare launch plays as the game's offline user and the world lives in
`Saves/1234567891011/GrappleLive`. **With Steam running, the game signs in as the Steam user**, so
the world lives beside the player's saves (as Thermodynamics' `ThermoLive` does) and that user's
`LastSession.sbl` is swapped for the load and restored straight after. `LT_PROFILE` overrides the
choice.

`lt` checks the session path the moment the game logs "Loading session:", and stops the game at
once if it is anything but `GrappleLive`. If the load fails it stops the game *before* putting the
player's `LastSession.sbl` back, so a game left at the menu can never continue the player's world. That check exists because the first run of this harness, written for
the offline profile, loaded the player's own last world while Steam was running.

## What it needs

* gamescope, gcc, Xlib and XTest headers, Pillow, `pactl`.
* `GrappleHook/mdk.local.ini` with `binarypath` and `output`: the game and the Proton prefix are
  derived from them.
* The Proton that Steam maps to the game in `config.vdf` `CompatToolMapping`. `LIVE_PROTON`,
  `LIVE_RUNTIME`, `LIVE_STEAM`, `LIVE_WIDTH` and `LIVE_HEIGHT` override the guesses.
* The game not already running outside the harness.
