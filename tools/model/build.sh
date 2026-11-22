#!/usr/bin/env bash
# Builds the code-generated models into the mod (tools/model/README.md).
#   bash tools/model/build.sh            build every design, write .mwm files, icons and previews
#   bash tools/model/build.sh --preview  only run Blender (JSON and previews), write nothing to the mod
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
repo=$PWD
preview_only=false
[ "${1:-}" = --preview ] && preview_only=true

blender=(flatpak run org.blender.Blender)
command -v blender >/dev/null && blender=(blender)
tool=tools/model/MwmTool/bin/Release/net9.0/MwmTool.dll
models=GrappleHook/Content/Models/Cubes
icons=GrappleHook/Content/Textures/GUI/Icons/Cubes

# the game's Bin64, from the machine's MDK2 settings (the same file the mod build reads)
sebin=$(sed -n 's/^binarypath=//p' GrappleHook/mdk.local.ini 2>/dev/null | tr -d '\r' | head -1)
[ -n "$sebin" ] || { echo "set binarypath in GrappleHook/mdk.local.ini to the game's Bin64" >&2; exit 1; }
dotnet build tools/model/MwmTool -c Release -v quiet -nologo -p:SEBin="$sebin" >/dev/null
# the hook as an OBJ, so design previews can show it where the game rests it
[ -f out/model/ref/hook.obj ] || { mkdir -p out/model/ref; dotnet "$tool" obj GrappleHook/Content/Models/ammo/GrappleHookAmmo.mwm out/model/ref/hook.obj; }

# Each design writes out/model/<design>/<Large|Small>/<model>.json, one per model, and
# icon_<Name>.png per block; they go to Models/Cubes/<Large|Small>/ and the Cubes icons.
for script in tools/model/designs/*.py; do
  design=$(basename "$script" .py)
  outdir=$repo/out/model/$design
  rm -rf "$outdir"/Large "$outdir"/Small "$outdir"/icon_*.png
  "${blender[@]}" -b --python "$script" -- "$outdir" 2>&1 |
    grep -E '^(exported|rendered)|Error|Traceback|^  File' | grep -v OpenColorIO || true
  ls "$outdir"/*/*.json >/dev/null || { echo "$design: Blender wrote no models" >&2; exit 1; }
  $preview_only && continue
  for json in "$outdir"/*/*.json; do
    grid=$(basename "$(dirname "$json")")
    mkdir -p "$models/$grid"
    dotnet "$tool" build "$json" tools/model/materials.json "$models/$grid/$(basename "${json%.json}").mwm"
  done
  # block icons are 128x128 DXT5 with mipmaps, like the game's own
  for png in "$outdir"/icon_*.png; do
    name=$(basename "$png" .png); name=${name#icon_}
    magick "$png" -resize 128x128 -background none -gravity center -extent 128x128 -define dds:compression=dxt5 "$icons/$name.dds"
  done
done

# toolbar icons for the grapple's own terminal actions
$preview_only && exit 0
python3 tools/model/action_icons.py out/model/action_icons >/dev/null
mkdir -p GrappleHook/Content/Textures/GUI/Icons/Actions
cp out/model/action_icons/*.dds GrappleHook/Content/Textures/GUI/Icons/Actions/
