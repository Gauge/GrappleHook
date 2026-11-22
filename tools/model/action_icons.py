#!/usr/bin/env python3
"""Toolbar icons for the grapple's own terminal actions, in the style of the game's action icons
(Content/Textures/GUI/Icons/Actions): a pale tile with a thin blue-grey border and a bold black
pictogram, secondary parts in the blue-grey the vanilla Decrease icon uses.

  python3 tools/model/action_icons.py OUTDIR      writes <name>.png previews and <name>.dds

  GrappleDetach        the grappling hook (an anchor) struck through: let go
  GrappleWinchTighten  the winch drum, a black arrow on the rope pointing in: reel in
  GrappleWinchLoosen   the winch drum, a blue arrow on the rope pointing out: pay out
  GrappleWinchReset    the winch drum inside a circular reset arrow: stop the winch

Drawn at 8x and scaled down, so edges are antialiased like the game's. 64x64: the game draws
action icons small, and twice the vanilla 32 px stays sharp on high resolution screens.
"""
import math
import os
import subprocess
import sys

from PIL import Image, ImageDraw

SIZE = 64
SS = 8                                  # supersampling
TILE = (228, 235, 239, 255)             # sampled from the vanilla icons
EDGE_OUT = (74, 121, 148, 70)
EDGE_IN = (129, 161, 181, 150)
INK = (0, 0, 0, 255)
SECOND = (99, 142, 165, 255)            # the vanilla "lesser" blue


def canvas():
    img = Image.new('RGBA', (SIZE * SS, SIZE * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    s = SS
    d.rectangle((0, 0, SIZE * s - 1, SIZE * s - 1), fill=EDGE_OUT)
    d.rectangle((2 * s, 2 * s, (SIZE - 2) * s - 1, (SIZE - 2) * s - 1), fill=EDGE_IN)
    d.rectangle((4 * s, 4 * s, (SIZE - 4) * s - 1, (SIZE - 4) * s - 1), fill=TILE)
    return img, d


def P(*xy):
    """Icon units (0..64) to canvas pixels."""
    return tuple(v * SS for v in xy)


def line(d, pts, w, fill=INK):
    d.line([P(*p) for p in pts], fill=fill, width=int(w * SS), joint='curve')
    for p in (pts[0], pts[-1]):            # round caps
        r = w / 2
        d.ellipse(P(p[0] - r, p[1] - r, p[0] + r, p[1] + r), fill=fill)


def disc(d, cx, cy, r, fill=INK):
    d.ellipse(P(cx - r, cy - r, cx + r, cy + r), fill=fill)


def arrowhead(d, tip, direction, length, width, fill=INK):
    """Filled triangle with its point at tip, pointing along direction (degrees, 0 = right)."""
    a = math.radians(direction)
    ux, uy = math.cos(a), math.sin(a)
    bx, by = tip[0] - ux * length, tip[1] - uy * length
    px, py = -uy * width / 2, ux * width / 2
    d.polygon([P(*tip), P(bx + px, by + py), P(bx - px, by - py)], fill=fill)


def drum(d, cx, cy, r):
    """The winch drum end-on: a black wheel with a pale hub and six spokes' worth of bolts."""
    disc(d, cx, cy, r)
    disc(d, cx, cy, r * 0.42, TILE)
    disc(d, cx, cy, r * 0.2)
    for i in range(6):
        a = 2 * math.pi * i / 6
        disc(d, cx + math.cos(a) * r * 0.7, cy + math.sin(a) * r * 0.7, r * 0.09, TILE)


def winch(d, arrow):
    """Drum at the lower left, rope leaving its top towards the right, one big arrowhead on the
    rope. The game draws these at about 20 px, so the arrow carries the meaning twice over:
    reel in points at the drum in black, pay out points away in the vanilla "less" blue."""
    cx, cy, r = 17, 41, 12
    drum(d, cx, cy, r)
    rope_y = cy - r + 2
    if arrow == 'in':
        line(d, [(cx, rope_y), (58, rope_y)], 4.5)
        arrowhead(d, (29, rope_y), 180, 18, 26)
    else:
        line(d, [(cx, rope_y), (40, rope_y)], 4.5)
        line(d, [(40, rope_y), (44, rope_y)], 4.5, SECOND)
        arrowhead(d, (60, rope_y), 0, 18, 26, SECOND)


def reset(d):
    """The drum inside a circular arrow running clockwise, head at the top: the vanilla Reset."""
    cx, cy, r = 32, 34, 20
    drum(d, cx, cy, 10)
    start, end = -50, 245                   # Pillow arcs run clockwise on screen
    d.arc(P(cx - r, cy - r, cx + r, cy + r), start=start, end=end, fill=INK, width=int(4.5 * SS))
    a = math.radians(end)
    ex, ey = cx + math.cos(a) * r, cy + math.sin(a) * r
    heading = end + 90                      # clockwise tangent at the arc's end
    h = math.radians(heading)
    arrowhead(d, (ex + math.cos(h) * 9, ey + math.sin(h) * 9), heading, 12, 14)


def anchor(d, hx):
    """The grappling hook as an anchor: ring, shank, stock, curved arms ending in barbs."""
    ring_cy, ring_r = 14, 5
    d.ellipse(P(hx - ring_r, ring_cy - ring_r, hx + ring_r, ring_cy + ring_r), outline=INK, width=int(3.5 * SS))
    foot = 49
    line(d, [(hx, ring_cy + ring_r), (hx, foot)], 4.5)
    line(d, [(hx - 8, 26), (hx + 8, 26)], 4)            # the stock across the shank
    ar = 16                                             # arms: the lower half of a circle
    acy = foot - ar + 2
    d.arc(P(hx - ar, acy - ar, hx + ar, acy + ar), start=10, end=170, fill=INK, width=int(4.5 * SS))
    for ang in (10, 170):
        a = math.radians(ang)
        bx, by = hx + math.cos(a) * ar, acy + math.sin(a) * ar
        arrowhead(d, (bx + (2 if ang < 90 else -2), by - 9), -90 + (25 if ang < 90 else -25), 10, 10)


def detach(d):
    """The anchor struck through, top left to bottom right: the hook is let go. The slash has a
    pale halo where it crosses the anchor, so both still read at toolbar size."""
    anchor(d, 32)
    a, b = (11, 11), (53, 53)
    line(d, [a, b], 7.5, TILE)
    line(d, [a, b], 4.5)


ICONS = {
    'GrappleDetach': detach,
    'GrappleWinchTighten': lambda d: winch(d, 'in'),
    'GrappleWinchLoosen': lambda d: winch(d, 'out'),
    'GrappleWinchReset': reset,
}


def main(outdir):
    os.makedirs(outdir, exist_ok=True)
    for name, draw in ICONS.items():
        img, d = canvas()
        draw(d)
        img = img.resize((SIZE, SIZE), Image.LANCZOS)
        png = os.path.join(outdir, name + '.png')
        img.save(png)
        # uncompressed: block compression smears a 64 px pictogram
        subprocess.run(['magick', png, '-define', 'dds:compression=none', '-define', 'dds:mipmaps=0',
                        os.path.join(outdir, name + '.dds')], check=True)
        print('wrote', name)


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else '.')
