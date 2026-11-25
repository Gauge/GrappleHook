#!/usr/bin/env python3
"""Converts the game textures a set of materials uses into PNGs Blender can read.

  python3 tools/model/textures.py <game Content dir> <materials.json> <names.txt> <outdir> [max size]

names.txt lists material names, one per line (the usemtl names of the OBJ files to render).
Each texture becomes <outdir>/<basename>.png, RGBA as the game packs it (colour/metalness,
normal/gloss, add maps, alpha masks), scaled down to max size (default 2048). The game's DDS files
are BC7 and BC4; Pillow decodes both. File names are matched without regard to case, as the
game's content folders differ in case on Linux.
"""
import json
import os
import sys

from PIL import Image


def find(content, rel):
    """The file under content for a Windows-style relative path, ignoring case at every level."""
    here = content
    for part in rel.replace('\\', '/').split('/'):
        entries = {e.lower(): e for e in os.listdir(here)}
        name = entries.get(part.lower())
        if name is None:
            return None
        here = os.path.join(here, name)
    return here


def main(content, materials, names, outdir, size=2048):
    os.makedirs(outdir, exist_ok=True)
    lib = json.load(open(materials))
    wanted = [n for n in open(names).read().split() if n]
    done = set()
    for name in wanted:
        for rel in lib.get(name, {}).get('textures', {}).values():
            base = os.path.splitext(os.path.basename(rel.replace('\\', '/')))[0]
            if base.lower() in done:
                continue
            done.add(base.lower())
            out = os.path.join(outdir, base + '.png')
            if os.path.exists(out):
                continue
            src = find(content, rel)
            if src is None:
                print('missing', rel)
                continue
            im = Image.open(src)
            im.load()
            if max(im.size) > size:
                im = im.resize((size, size * im.size[1] // im.size[0]), Image.LANCZOS)
            im.save(out)
            print('wrote', out, im.mode, im.size)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4], int(sys.argv[5]) if len(sys.argv) > 5 else 2048)
