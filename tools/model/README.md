# tools/model: block models built from code

The small grid turret's and the fixed launchers' models are generated, not hand-modelled. Each
block has a Blender script under `designs/` that builds its geometry from primitives, and
`MwmTool` writes the `.mwm` files with the game's own model exporter. No MwmBuilder, Havok tools
or Wine are involved, so the whole chain runs on Linux.

```
bash tools/model/build.sh            # Blender -> mesh JSON -> .mwm into the mod, icon, previews
bash tools/model/build.sh --preview  # only Blender: mesh JSON and preview renders, nothing written to the mod
```

Each design writes its models as `out/model/<design>/<Large|Small>/<model>.json`, converted to
`Models/Cubes/<Large|Small>/<model>.mwm`, and `icon_<Name>.png` per block, converted to
`Textures/GUI/Icons/Cubes/<Name>.dds`. Previews land beside them: four views of each block
assembled (the fixed launchers with the real hook where the game rests it) and its build stages.

## Pieces

| Path | What it does |
| --- | --- |
| `designs/grapple_turret_small.py` | The small grid turret: fixed base, turning housing, tilting launcher, build stages, icon. Its docstring gives the dimensions and why. |
| `designs/grapple_fixed.py` | Both fixed launchers (1x1x2 large, 1x1x4 small): launch tubes, build stages, icons. |
| `semodel.py` | Helpers for designs: boxes, prisms, cylinders, outlines, bevels, dummies, the export to mesh JSON and the preview renders. |
| `materials.json` | The game materials a design can use, each copied from a model the game or this mod ships (texture paths, technique). |
| `MwmTool/` | .NET 9 tool on the game's DLLs (`VRage.Render`, `VRage`, `VRage.Math`, `VRage.Library`). |
| `render.py` | Renders OBJ files side by side, for comparing against existing models. |
| `action_icons.py` | The toolbar icons for the grapple's terminal actions, drawn with Pillow in the game's action icon style; `build.sh` copies them into `Textures/GUI/Icons/Actions`. |
| `build.sh` | Runs each design, converts every model, writes the icons. |

`MwmTool` commands, paths to `.mwm` files absolute or relative:

```
dotnet tools/model/MwmTool/bin/Release/net9.0/MwmTool.dll build <mesh.json> tools/model/materials.json <out.mwm>
                                                           dump <model.mwm> [collision.out]  # tags, dummies, materials, LODs
                                                           obj <model.mwm> <out.obj> [elevation]  # with its subparts placed
                                                           probe <model.mwm>                 # winding, UV density per material
                                                           materials <out.json> <model.mwm...>
```

## Conventions the files rely on

These were read out of the game's code and shipped models, and a model that breaks them loads
wrong or not at all:

- **Frames.** Designs work in Blender's frame (Z up, the block's front +Y). The export turns that
  into the game's (Y up, front -Z) with a rotation, so winding is kept.
- **Winding.** The game draws triangles clockwise as seen from outside. Blender's are
  counter-clockwise; `MwmTool build` flips them.
- **Version tag.** The importer reads the format version only from the first `Debug` string, and
  the exporter appends it last, so `Debug` must otherwise be empty.
- **Dummies.** `subpart_MissileTurretBase1` (in the block model) and `subpart_MissileTurretBarrels`
  (in the base) carry `file=` naming the subpart model. Their matrices are used normalised.
  `muzzle_missile_001` and `camera` sit in the barrels model. Blender's `.001` name suffixes are
  stripped on export.
- **Where the hook rests.** The game takes a gun's muzzle from the dummy named for its ammo type,
  and the grapple ammo is a projectile type, so only `muzzle_projectile*` counts; without one it
  is the gun's own frame (a turret's barrel pivot, a fixed gun's block centre). The hook's tail
  rests at the muzzle, its centre 1.1 m ahead and its tip 2.24 m ahead.
- **Collision.** The models carry no Havok collision, as the large turret's don't. The game then
  gives the block a box the size of the whole block.
- **Materials.** Faces take the name of their material slot. `PaintedMetal_Colorable` is the
  paintable one; the rest keep their colour. UVs are box projected at the texel density each
  material has in the large turret's meshes (`MwmTool probe`).

## Requirements

Blender (the Flathub `org.blender.Blender` works; it can read the repository but not `/tmp`), the
.NET 9 SDK, ImageMagick for the icon, and the game installed at the path in
`GrappleHook/mdk.local.ini` (`binarypath`, the game's Bin64; `build.sh` passes it to `MwmTool`).
